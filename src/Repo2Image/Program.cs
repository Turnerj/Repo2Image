using Repo2Image;
using Statiq.App;
using Statiq.Core;

await Bootstrapper
	.Factory
	.CreateDefault(args)
	.BuildPipeline("Repo2Image", builder =>
	{
		builder.WithInputReadFiles("*/*.json")
			.WithProcessModules(new ParseJson(), new GenerateImage())
			.WithOutputWriteFiles();
	})
	.BuildPipeline("Configuration", builder =>
	{
		builder.WithInputReadFiles("_headers")
			.WithOutputWriteFiles();
	})
	.RunAsync();