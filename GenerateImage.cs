using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Octokit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.Fonts;
using System.Reflection;
using Microsoft.Net.Http.Headers;
using Microsoft.Extensions.Primitives;
using System.Net.Http;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.Drawing;

namespace Repo2Image
{
	public class GenerateImage
	{
		private readonly HttpClient HttpClient;
		private const int MaxIconWidth = 200;
		private const int MaxIconHeight = 200;

		public GenerateImage(HttpClient httpClient)
		{
			HttpClient = httpClient;
		}

		private static readonly Image StarImage;
		private static readonly Image ForkImage;
		private static readonly FontFamily FontFamily;

		static GenerateImage()
		{
			var assembly = Assembly.GetExecutingAssembly();

			var fontCollection = new FontCollection();
			using var fontStream = assembly.GetManifestResourceStream("Repo2Image.fonts.PatuaOne-Regular.ttf");
			FontFamily = fontCollection.Add(fontStream);

			using var starStream = assembly.GetManifestResourceStream("Repo2Image.images.star-solid.png");
			StarImage = Image.Load<Rgba32>(starStream);
			using var forkStream = assembly.GetManifestResourceStream("Repo2Image.images.code-branch-solid.png");
			ForkImage = Image.Load<Rgba32>(forkStream);
		}

		[FunctionName("GenerateImage")]
		public async Task<IActionResult> Run(
			[HttpTrigger(AuthorizationLevel.Anonymous, Route = "{owner}/{repoName}/image.png")] HttpRequest req,
			ILogger log,
			string owner,
			string repoName
		)
		{
			try
			{
				var github = new GitHubClient(new ProductHeaderValue("Repo2Image"))
				{
					Credentials = new Credentials(Environment.GetEnvironmentVariable("GitHubToken"))
				};

				var repoTask = github.Repository.Get(owner, repoName);
				var vibrantColoursTask = GetVibrantColoursAsync(log, owner, repoName);

				await Task.WhenAll(repoTask, vibrantColoursTask);

				var repo = await repoTask;
				var vibrantColours = await vibrantColoursTask;

				//Generate image
				using var image = new Image<Rgba32>(420, 80);
				image.Mutate(x =>
				{
					if (vibrantColours.Length > 0)
					{
						var colouredAreaGradient = new LinearGradientBrush(
							new PointF(0, 0),
							new PointF(image.Width, 0),
							GradientRepetitionMode.None,
							ImageColourSampler.GetColourStops(vibrantColours)
						);
						x.Fill(colouredAreaGradient);
						x.Fill(Color.FromRgba(0, 0, 0, (byte)(255 * 0.1)));
					}
					else
					{
						var colouredAreaGradient = new LinearGradientBrush(
							new PointF(0, 0),
							new PointF(image.Width, 0),
							GradientRepetitionMode.None,
							new ColorStop[]
							{
								new(0f, Color.ParseHex("#605858")),
								new(1f, Color.ParseHex("#8C8181"))
							}
						);
						x.Fill(colouredAreaGradient);
					}

					x.DrawText(
						$"{repo.Owner.Login}'s",
						FontFamily.CreateFont(20f),
						Color.FromRgba(0, 0, 0, (byte)(255*0.6)),
						new PointF(15f, 15f)
					);
					x.DrawText(
						repo.Name,
						FontFamily.CreateFont(24f),
						Color.White,
						new PointF(15f, 40f)
					);

					x.DrawImage(StarImage, Point.Subtract(new Point(340, 17), new Size(StarImage.Width / 2, 0)), 0.7f);
					DrawText(
						x,
						new TextOptions(FontFamily.CreateFont(16f))
						{
							HorizontalAlignment = HorizontalAlignment.Center,
							Origin = new PointF(340, 47)
						},
						GetNumberString(repo.StargazersCount),
						Color.White
					);
					x.DrawImage(ForkImage, Point.Subtract(new Point(390, 17), new Size(ForkImage.Width / 2, 0)), 0.7f);
					DrawText(
						x,
						new TextOptions(FontFamily.CreateFont(16f))
						{
							HorizontalAlignment = HorizontalAlignment.Center,
							Origin = new PointF(390f, 47)
						},
						GetNumberString(repo.ForksCount),
						Color.White
					);
				});

				//Output image
				var response = req.HttpContext.Response;
				response.ContentType = "image/png";

				var headers = response.GetTypedHeaders();
				headers.CacheControl = new CacheControlHeaderValue()
				{
					MaxAge = TimeSpan.FromHours(6),
					MustRevalidate = true
				};
				headers.Expires = DateTimeOffset.UtcNow.AddHours(6);
				headers.ETag = new EntityTagHeaderValue(new StringSegment($"\"{owner}-{repoName}-{repo.StargazersCount}-{repo.ForksCount}\""));
				headers.LastModified = DateTime.UtcNow;

				await image.SaveAsync(response.Body, new PngEncoder
				{
					ColorType = PngColorType.Palette,
					BitDepth = PngBitDepth.Bit8,
					CompressionLevel = PngCompressionLevel.BestCompression
				});

				return new EmptyResult();
			}
			catch (Exception ex)
			{
				log.LogError(ex, "Unable to generate image");;
				return new BadRequestObjectResult("Unable to generate image");
			}
		}

		private async Task<Rgb[]> GetVibrantColoursAsync(ILogger log, string owner, string repoName)
		{
			try
			{
				using var imageStream = await HttpClient.GetStreamAsync($"https://raw.githubusercontent.com/{owner}/{repoName}/main/images/icon.png");
				var iconImage = await Image.LoadAsync<Rgb24>(imageStream);
				if (iconImage.Width <= MaxIconWidth && iconImage.Height <= MaxIconHeight)
				{
					return iconImage.GetVibrantColours();
				}
			}
			catch (Exception ex)
			{
				log.LogInformation(ex, "Repository icon unavailable");
			}

			return Array.Empty<Rgb>();
		}

		private static string GetNumberString(int number)
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

		private static void DrawText(IImageProcessingContext source, TextOptions textOptions, string text, Color color)
		{
			var textPath = TextBuilder.GenerateGlyphs(
				text,
				textOptions
			);
			source.Fill(color, textPath);
		}
	}
}
