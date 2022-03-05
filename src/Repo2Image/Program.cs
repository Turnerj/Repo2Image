using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repo2Image;
using Statiq.App;
using Statiq.Common;
using Statiq.Core;

await Bootstrapper
	.Factory
	.CreateDefault(args)
	.BuildConfiguration(c =>
	{
		c.AddUserSecrets<Program>();
		c.AddEnvironmentVariables();
	})
	.ConfigureServices(s =>
	{
		s.AddSingleton<GenerateImage>();
	})
	.BuildPipeline("Repo2Image", builder =>
	{
		builder.WithInputReadFiles("*/*.json")
			.WithProcessModules(new ParseJson(), builder.Services.GetRequiredService<GenerateImage>())
			.WithOutputWriteFiles();
	})
	.BuildPipeline("Configuration", builder =>
	{
		builder.WithInputReadFiles("_headers")
			.WithOutputWriteFiles();
	})
	.RunAsync();