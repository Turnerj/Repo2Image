using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Octokit;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Statiq.Common;
using System.Security.Cryptography;
using TurnerSoftware.Vibrancy;

namespace Repo2Image;

internal class GenerateImage : ParallelModule
{
	private const int Width = 420;
	private const int Height = 80;
	private static readonly ColorSpaceConverter ColorSpaceConverter = new();

	private static readonly Palette Palette = new(new PaletteOptions(new SwatchDefinition[]
	{
		SwatchDefinition.DarkVibrant with
		{
			MinSaturation = 0.6f
		},
		SwatchDefinition.Vibrant with
		{
			MinSaturation = 0.6f
		}
	}));

	private readonly HttpClient HttpClient;
	private readonly GitHubClient GitHub;
	private readonly Image StarImage;
	private readonly Image ForkImage;
	private readonly Image DownloadImage;
	private readonly FontFamily FontFamily;

	public GenerateImage()
	{
		HttpClient = new HttpClient();
		GitHub = new GitHubClient(new ProductHeaderValue("Repo2Image"))
		{
			//Credentials = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
		};

		var fontCollection = new FontCollection();
		FontFamily = fontCollection.Add("fonts/PatuaOne-Regular.ttf");

		StarImage = Image.Load<Rgba32>("images/star-solid.png");
		ForkImage = Image.Load<Rgba32>("images/code-branch-solid.png");
		DownloadImage = Image.Load<Rgba32>("images/download-solid.png");
	}

	protected override async Task<IEnumerable<IDocument>> ExecuteInputAsync(IDocument input, IExecutionContext context) => new[]
	{
		await CreateImageAsync(input, context)
	};

	private async ValueTask<IDocument> CreateImageAsync(IDocument input, IExecutionContext context)
	{
		var projectDetails = await GetProjectDetailsAsync(input, context);

		//Generate image
		using var image = new Image<Rgba32>(Width, Height);
		image.Mutate(x =>
		{
			if (projectDetails.VibrantColours.Length > 0)
			{
				DrawGradientBackground(x, GetColourStops(projectDetails.VibrantColours));
			}
			else
			{
				DrawGradientBackground(x, GetDefaultBackground());
			}

			x.DrawText(
				$"{projectDetails.Owner}'s",
				FontFamily.CreateFont(20f),
				Color.FromRgba(0, 0, 0, (byte)(255 * 0.7)),
				new PointF(15f, 15f)
			);
			DrawTextWithShadow(
				x,
				projectDetails.RepositoryName,
				FontFamily.CreateFont(24f),
				Color.White,
				new Point(15, 40),
				Color.FromRgba(0, 0, 0, 122),
				new Point(1, 1)
			);

			DrawMetric(x, StarImage, 334, projectDetails.NumberOfStargazers);

			if (projectDetails.NumberOfDownloads > 0)
			{
				DrawMetric(x, DownloadImage, 387, projectDetails.NumberOfDownloads);
			}
			else
			{
				DrawMetric(x, ForkImage, 387, projectDetails.NumberOfForks);
			}
		});

		var output = new MemoryStream();
		await image.SaveAsync(output, new PngEncoder
		{
			ColorType = PngColorType.Palette,
			BitDepth = PngBitDepth.Bit8,
			CompressionLevel = PngCompressionLevel.BestCompression
		});
		return context.CreateDocument(
			input.Source,
			$"{projectDetails.Owner}/{projectDetails.RepositoryName}.png",
			context.GetContentProvider(output)
		);
	}
	private async Task<ProjectDetails> GetProjectDetailsAsync(IDocument input, IExecutionContext context)
	{
		var owner = input.Source.Parent.Name;
		var repoName = input.Source.FileNameWithoutExtension.Name;
		var packages = input.GetList("Packages", Array.Empty<string>());

		var repoTask = GitHub.Repository.Get(owner, repoName);
		var vibrantColoursTask = GetBackgroundColoursAsync(owner, repoName, input, context);
		var downloadCountTask = Task.FromResult(0L);

		if (packages.Count > 0)
		{
			downloadCountTask = GetDownloadCountAsync(packages);
		}
		
		//Get repository details, background colours and download counts at the same time
		await Task.WhenAll(repoTask, vibrantColoursTask, downloadCountTask);
		var repo = await repoTask;
		var vibrantColours = await vibrantColoursTask;
		var downloadCount = await downloadCountTask;

		var result = new ProjectDetails
		{
			Owner = repo.Owner.Login,
			RepositoryName = repo.Name,
			NumberOfForks = repo.ForksCount,
			NumberOfStargazers = repo.StargazersCount,
			NumberOfDownloads = downloadCount,
			VibrantColours = vibrantColours
		};
		return result;
	}

	private async Task<Rgb[]> GetBackgroundColoursAsync(string owner, string repoName, IDocument document, IExecutionContext context)
	{
		if (document.TryGetValue<string>("Background", out var background))
		{
			return new Rgb[]
			{
				Color.ParseHex(background).ToPixel<Rgb24>()
			};
		}

		try
		{
			var imageUrl = document.GetString("ImageUrl", $"https://raw.githubusercontent.com/{owner}/{repoName}/main/images/icon.png");
			using var imageStream = await HttpClient.GetStreamAsync(imageUrl);
			var iconImage = await Image.LoadAsync<Rgb24>(imageStream);

			var swatches = Palette.GetSwatches(iconImage);
			var swatch = swatches[1];
			if (swatch.Count == 0)
			{
				swatch = swatches[0];
			}

			return swatch.GetColors()
				.OrderBy(c => c.Hsv.H)
				.Select(c => c.Rgb)
				.ToArray();
		}
		catch (Exception ex)
		{
			context.LogWarning(document, ex.Message);
			return Array.Empty<Rgb>();
		}
	}

	private static ColorStop[] GetDefaultBackground()
	{
		var hue = RandomNumberGenerator.GetInt32(360);
		var hsvA = new Hsv(hue, .18f, .37f);
		var hsvB = new Hsv(hue, .18f, .54f);
		var colourA = (Rgb24)ColorSpaceConverter.ToRgb(hsvA);
		var colourB = (Rgb24)ColorSpaceConverter.ToRgb(hsvB);
		return new ColorStop[]
		{
			new(0f, colourA),
			new(1f, colourB)
		};
	}

	private static ColorStop[] GetColourStops(Rgb[] colours)
	{
		var colourStops = new ColorStop[colours.Length];
		var stopDistance = 1f;
		if (colours.Length > 1)
		{
			stopDistance /= (colours.Length - 1);
		}

		for (var i = 0; i < colours.Length; i++)
		{
			var colour = colours[i];
			colourStops[i] = new ColorStop(
				stopDistance * i,
				Color.FromRgb(
					(byte)(colour.R * 255),
					(byte)(colour.G * 255),
					(byte)(colour.B * 255)
				)
			);
		}
		return colourStops;
	}

	private static void DrawGradientBackground(IImageProcessingContext imageProcessingContext, ColorStop[] colorStops)
	{
		var gradient = new LinearGradientBrush(
			new PointF(0, 0),
			new PointF(Width, 0),
			GradientRepetitionMode.None,
			colorStops
		);
		imageProcessingContext.Fill(gradient);
	}

	private static async Task<long> GetDownloadCountAsync(IReadOnlyList<string> packages)
	{
		var sourceRepository = NuGet.Protocol.Core.Types.Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
		var packageSearchResource = sourceRepository.GetResource<PackageSearchResource>();
		var packageResults = await packageSearchResource.SearchAsync(
			string.Join(' ', packages.Select(p => $"packageid:{p}")),
			new SearchFilter(includePrerelease: true),
			skip: 0,
			take: packages.Count,
			log: NullLogger.Instance,
			cancellationToken: CancellationToken.None
		);
		return packageResults.Sum(p => p.DownloadCount ?? 0);
	}

	private static void DrawTextWithShadow(IImageProcessingContext imageProcessingContext, string text, Font font, Color color, Point location, Color shadow, Point offset)
		=> DrawTextWithShadow(imageProcessingContext, text, new TextOptions(font) { Origin = location }, color, shadow, offset);

	private static void DrawTextWithShadow(IImageProcessingContext imageProcessingContext, string text, TextOptions textOptions, Color color, Color shadow, Point offset)
	{
		//Shadow
		textOptions.Origin += offset;
		imageProcessingContext.DrawText(
			textOptions,
			text,
			shadow
		);
		//Original
		textOptions.Origin -= offset;
		imageProcessingContext.DrawText(
			textOptions,
			text,
			color
		);
	}

	private void DrawMetric(IImageProcessingContext imageProcessingContext, Image icon, int x, long value)
	{
		imageProcessingContext.DrawImage(icon, Point.Subtract(new Point(x, 17), new Size(icon.Width / 2, 0)), 0.7f);
		DrawTextWithShadow(
			imageProcessingContext,
			FormatNumber(value),
			new TextOptions(FontFamily.CreateFont(16f))
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				Origin = new Point(x, 47)
			},
			Color.White,
			Color.FromRgba(0, 0, 0, 122),
			new Point(1, 1)
		);
	}

	private static string FormatNumber(long number)
	{
		return number switch
		{
			>= 1_000_000 => (number / 1_000_000d).ToString("0.0m"),
			>= 1_000 => (number / 1_000d).ToString("0.0k"),
			_ => number.ToString()
		};
	}
}
