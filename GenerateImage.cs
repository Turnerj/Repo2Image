using System;
using System.IO;
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
using TurnerSoftware.Vibrancy;
using System.Linq;

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
		//TODO: Look at using Vibrancy
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
				var repo = await github.Repository.Get(owner, repoName);

				var fontCollection = new FontCollection();
				using var fontStream = Assembly.GetExecutingAssembly()
					.GetManifestResourceStream("Repo2Image.fonts.PatuaOne-Regular.ttf");
				var fontFamily = fontCollection.Install(fontStream);

				using var starStream = Assembly.GetExecutingAssembly()
					.GetManifestResourceStream("Repo2Image.images.star-solid.png");
				using var starImage = await Image.LoadAsync<Rgba32>(starStream);
				using var forkStream = Assembly.GetExecutingAssembly()
					.GetManifestResourceStream("Repo2Image.images.code-branch-solid.png");
				using var forkImage = await Image.LoadAsync<Rgba32>(forkStream);

				Image<Rgb24> iconImage = null;
				Rgb[] vibrantColours = null;
				try
				{
					using var imageStream = await HttpClient.GetStreamAsync($"https://github.com/{repo.Owner.Login}/{repo.Name}/raw/main/images/icon.png");
					iconImage = await Image.LoadAsync<Rgb24>(imageStream);
					if (iconImage.Width <= MaxIconWidth && iconImage.Height <= MaxIconHeight)
					{
						vibrantColours = iconImage.GetVibrantColours();
					}
				}
				catch (Exception ex)
				{
					log.LogInformation(ex, "Repository icon unavailable");
				}

				using var image = new Image<Rgba32>(420, 80);
				image.Mutate(x =>
				{
					if (vibrantColours is not null && vibrantColours.Length > 0)
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
						fontFamily.CreateFont(20f),
						Color.FromRgba(0, 0, 0, (byte)(255*0.6)),
						new PointF(15f, 15f)
					);
					x.DrawText(
						repo.Name,
						fontFamily.CreateFont(24f),
						Color.White,
						new PointF(15f, 40f)
					);

					x.DrawImage(starImage, Point.Subtract(new Point(340, 17), new Size(starImage.Width / 2, 0)), 0.7f);
					x.DrawText(
						new DrawingOptions
						{
							TextOptions = new TextOptions
							{
								HorizontalAlignment = HorizontalAlignment.Center
							}
						},
						GetNumberString(repo.StargazersCount),
						fontFamily.CreateFont(16f),
						Color.White,
						new PointF(340f, 47f)
					);
					x.DrawImage(forkImage, Point.Subtract(new Point(390, 17), new Size(forkImage.Width / 2, 0)), 0.7f);
					x.DrawText(
						new DrawingOptions
						{
							TextOptions = new TextOptions
							{
								HorizontalAlignment = HorizontalAlignment.Center
							}
						},
						GetNumberString(repo.ForksCount),
						fontFamily.CreateFont(16f),
						Color.White,
						new PointF(390f, 47f)
					);
				});

				var stream = new MemoryStream();
				await image.SaveAsync(stream, new PngEncoder
				{
					ColorType = PngColorType.Palette,
					BitDepth = PngBitDepth.Bit8,
					CompressionLevel = PngCompressionLevel.BestCompression
				});
				stream.Seek(0, SeekOrigin.Begin);

				var headers = req.HttpContext.Response.GetTypedHeaders();
				headers.CacheControl = new CacheControlHeaderValue()
				{
					MaxAge = TimeSpan.FromHours(6),
					MustRevalidate = true
				};
				headers.Expires = DateTimeOffset.UtcNow.AddHours(6);
				return new FileStreamResult(stream, "image/png")
				{
					EntityTag = new EntityTagHeaderValue(new StringSegment($"\"{owner}-{repoName}-{repo.StargazersCount}-{repo.ForksCount}\"")),
					LastModified = DateTime.UtcNow
				};
			}
			catch (Exception ex)
			{
				log.LogError(ex, "Unable to generate image");;
				return new BadRequestObjectResult("Unable to generate image");
			}
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
	}
}
