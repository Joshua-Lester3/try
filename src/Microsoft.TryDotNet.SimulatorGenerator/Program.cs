using System.CommandLine;
using Microsoft.TryDotNet.SimulatorGenerator;

var existingOnlyOption = new Option<DirectoryInfo>("--destination-folder")
{
    Description = "Location to write the simulator files",
    Required = true
}.AcceptExistingOnly();

var command = new RootCommand();
command.Options.Add(existingOnlyOption);

command.SetAction(async (ParseResult parseResult, CancellationToken token) =>
{
    var destinationFolder = parseResult.GetValue(existingOnlyOption)!;
    await ApiEndpointSimulatorGenerator.CreateScenarioFiles(destinationFolder);
});

return await command.Parse(args).InvokeAsync();
