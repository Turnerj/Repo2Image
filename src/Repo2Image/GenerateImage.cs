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
			Credentials = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
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
		var owner = input.Source.Parent.Name;
		var repoName = input.Source.FileNameWithoutExtension.Name;
		var packages = input.GetList("Packages", Array.Empty<string>());

		var repoTask = GitHub.Repository.Get(owner, repoName);
		var vibrantColoursTask = GetBackgroundColoursAsync(owner, repoName, input, context);
		var packageCountTask = new ValueTask<long>(0);

		if (packages.Count > 0)
		{
			packageCountTask = GetDownloadCountAsync(packages);
		}

		//Get repository details and background colours at the same time
		await Task.WhenAll(repoTask, vibrantColoursTask);
		var repo = await repoTask;
		var vibrantColours = await vibrantColoursTask;
		var downloadCount = await packageCountTask;

		//Generate image
		using var image = new Image<Rgba32>(Width, Height);
		image.Mutate(x =>
		{
			if (vibrantColours.Length > 0)
			{
				DrawGradientBackground(x, GetColourStops(vibrantColours));
				x.Fill(Color.FromRgba(0, 0, 0, (byte)(255 * 0.1)));
			}
			else
			{
				DrawGradientBackground(x, GetDefaultBackground());
			}

			x.DrawText(
				$"{repo.Owner.Login}'s",
				FontFamily.CreateFont(20f),
				Color.FromRgba(0, 0, 0, (byte)(255 * 0.6)),
				new PointF(15f, 15f)
			);
			x.DrawText(
				repo.Name,
				FontFamily.CreateFont(24f),
				Color.White,
				new PointF(15f, 40f)
			);

			DrawMetric(x, StarImage, 334, repo.StargazersCount);

			if (downloadCount > 0)
			{
				DrawMetric(x, DownloadImage, 387, downloadCount);
			}
			else
			{
				DrawMetric(x, ForkImage, 387, repo.ForksCount);
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
			$"{repo.Owner.Login}/{repo.Name}.png",
			context.GetContentProvider(output)
		);
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

	private static async ValueTask<long> GetDownloadCountAsync(IReadOnlyList<string> packages)
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

	private void DrawMetric(IImageProcessingContext imageProcessingContext, Image icon, int x, long value)
	{
		imageProcessingContext.DrawImage(icon, Point.Subtract(new Point(x, 17), new Size(icon.Width / 2, 0)), 0.6f);
		imageProcessingContext.DrawText(
			new TextOptions(FontFamily.CreateFont(16f))
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				Origin = new PointF(x, 47)
			},
			FormatNumber(value),
			Color.White
		);
	}

	private static string FormatNumber(long number)
	{
		if (number < 1000)
		{
			return number.ToString();
		}
		else
		{
			return (number / 1000d).ToString("0.0k");
		}
	}
}
