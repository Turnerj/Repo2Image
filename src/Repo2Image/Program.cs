using Repo2Image;
using Statiq.App;

await Bootstrapper
	.Factory
	.CreateDefault(args)
	.BuildPipeline("Repo2Image", builder =>
	{
		builder.WithInputReadFiles("*/*.json")
			.WithProcessModules(new GenerateImage())
			.WithOutputWriteFiles();
	})
	.BuildPipeline("Configuration", builder =>
	{
		builder.WithInputReadFiles("_headers")
			.WithOutputWriteFiles();
	})
	.RunAsync();