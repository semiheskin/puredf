using System.Diagnostics;
using ByteSizeLib;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace puredf;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var builder = new ConfigurationBuilder()
            .AddJsonFile($"appsettings.json", true, true)
            .AddJsonFile($"appsettings.{env}.json", true, true);

        var appsettings = builder.Build();
        var flashArrays = FlashArraySettingsHelper.ConvertToSettings(appsettings.GetSection(Constants.AppSettingsFlashArraysSectionName));

        var dfCommandStartInfo = new ProcessStartInfo("df", "-h")
        {
            RedirectStandardOutput = true
        };

        var dfCommand = Process.Start(dfCommandStartInfo);

        var dfCommandOutput = string.Empty;
        using var dfCommandOutputReader = dfCommand!.StandardOutput;
        dfCommandOutput = dfCommandOutputReader!.ReadToEnd();

        var dfCommandOutputTable = new List<List<string>>();

        foreach (var row in dfCommandOutput!.Split('\n').Skip(1))
        {
            var rowToAdd = new List<string>();
            foreach (var col in row.Split(" "))
            {
                if (string.IsNullOrWhiteSpace(col))
                    continue;
                rowToAdd.Add(col);
            }
            dfCommandOutputTable.Add(rowToAdd);
        }

        //New line split adds an empty row at the end.
        if (dfCommandOutputTable.Last().Count == 0)
            dfCommandOutputTable.Remove(dfCommandOutputTable.Last());

        foreach (var row in dfCommandOutputTable)
        {
            //If filesystem column contains ':' character, it is a remote filesystem.
            if (row[0].Contains(':'))
            {
                //split[0] = ip or fqdn of the remote filesystem
                //split[1] = export name of the remote filesystem
                var split = row[0].Split(':');

                //remove '/' character from the export name
                split[1] = split[1].Remove(0, 1);

                var flashArray = flashArrays.FirstOrDefault(p => p.DataIpFqdn == split[0]);

                //Remote filesystem is mounted from a FlashArray
                if (flashArray != null)
                {
                    var client = new FlashArrayApi(flashArray.ManagementIpFqdn!);
                    if (await client.Login(flashArray.ClientId!, flashArray.KeyId!, flashArray.Issuer!, flashArray.Username!, flashArray.PrivateKeyPath!))
                    {
                        var directoryExportsResult = await client.GetDirectoryExports();
                        var directoryObjectsJson = JObject.Parse(directoryExportsResult);
                        var directoryJson = directoryObjectsJson["items"]!.Where(p => p["export_name"]!.ToString() == split[1]).FirstOrDefault();
                        var directoryId = directoryJson!["directory"]!["id"]!.ToString();

                        var getQuotaPoliciesForDirectoryResult = await client.GetQuotaPoliciesForDirectory(directoryId!);
                        var getQuotaPoliciesForDirectoryJson = JObject.Parse(getQuotaPoliciesForDirectoryResult);
                        var doesQuotaPolicyExists = getQuotaPoliciesForDirectoryJson["items"]!.Count() > 0;



                        if (!string.IsNullOrWhiteSpace(directoryId))
                        {

                            var arraySpaceUtilizationInfoResult = await client.GetArraySpaceInfo();
                            var arraySpaceUtilizationInfoJson = JObject.Parse(arraySpaceUtilizationInfoResult);
                            var totalCapacity = ByteSize.FromBytes(double.Parse(arraySpaceUtilizationInfoJson["items"]![0]!["capacity"]!.ToString()));

                            if (doesQuotaPolicyExists)
                            {
                                var policyId = getQuotaPoliciesForDirectoryJson["items"]![0]!["policy"]!["id"]!.ToString();
                                var getQuotaPolicyRulesResult = await client.GetQuotaPolicyRules(policyId!);
                                var getQuotaPolicyRulesJson = JObject.Parse(getQuotaPolicyRulesResult);

                                var quotaLimit = getQuotaPolicyRulesJson["items"]!
                                    .Where(p => bool.Parse(p["enforced"]!
                                    .ToString()) == true)
                                    .Select(p => ByteSize.FromBytes(double.Parse(p["quota_limit"]!.ToString())))
                                    .FirstOrDefault();

                                //There is an enforced quota for the directory and that quota is smaller than the total array size
                                //In this case quota is the limiting factor
                                //default df reporting is enough
                                if (quotaLimit != default && totalCapacity >= quotaLimit)
                                {
                                    continue;
                                }
                            }

                            //"Filesystem" = [0]
                            //"Size" =       [1]
                            //"Used" =       [2]
                            //"Avail" =      [3]
                            //"Use%" =       [4]
                            //"Mounted on" = [5]

                            row[1] = totalCapacity.ToString("#.#").Replace(" ", "").Replace("B", "");

                            var totalUsedCapacity = ByteSize.FromBytes(double.Parse(arraySpaceUtilizationInfoJson["items"]![0]!["space"]!["total_used"]!.ToString()));
                            var availableCapacity = totalCapacity - totalUsedCapacity;
                            row[3] = availableCapacity.ToString("#.#").Replace(" ", "").Replace("B", "");

                            var usedPercentage = totalUsedCapacity.Bytes / totalCapacity.Bytes;
                            row[4] = usedPercentage.ToString("P0");

                            var directorySpaceUtilizationResult = await client.GetDirectorySpaceUtilization(directoryId);
                            var directorySpaceUtilizationJson = JObject.Parse(directorySpaceUtilizationResult);
                            var physicalUsedCapacity = ByteSize.FromBytes(double.Parse(directorySpaceUtilizationJson["items"]![0]!["space"]!["total_physical"]!.ToString()));
                            row[2] = physicalUsedCapacity.ToString("#.#").Replace(" ", "").Replace("B", ""); ;
                        }
                    }
                    else
                    {
                        //TODO: Error message
                        return 1;
                    }
                }
            }
        }

        //Render the output
        var table = new Table();
        table.Border(TableBorder.None);

        table.AddColumn(new TableColumn("Filesystem").NoWrap());
        table.AddColumn(new TableColumn("Size"));
        table.AddColumn(new TableColumn("Used"));
        table.AddColumn(new TableColumn("Avail"));
        table.AddColumn(new TableColumn("Use%").RightAligned());
        table.AddColumn(new TableColumn("Mounted on").NoWrap());

        foreach (var row in dfCommandOutputTable)
            table.AddRow(row.ToArray());

        AnsiConsole.Write(table);

        return 0;
    }
}
