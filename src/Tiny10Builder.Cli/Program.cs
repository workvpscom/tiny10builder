using System.Runtime.Versioning;
using Tiny10Builder.Cli;

[assembly: SupportedOSPlatform("windows")]

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
return await CliApp.RunAsync(args, new BuildEngineAdapter(), Console.Out, Console.Error, cancel.Token);
