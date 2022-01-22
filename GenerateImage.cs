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

namespace Repo2Image
{
	public class GenerateImage
	{
		private readonly HttpClient HttpClient;

		public GenerateImage(HttpClient httpClient)
		{
			HttpClient = httpClient;
		}

		[FunctionName("GenerateImage")]
		public async Task<IActionResult> Run(
			[HttpTrigger(AuthorizationLevel.Anonymous, Route = "{owner}/{repoName}/image")] HttpRequest req,
			ILogger log,
			string owner,
			string repoName
		)
		{
			log.LogInformation("C# HTTP trigger function processed a request.");

			try
			{
				var github = new GitHubClient(new ProductHeaderValue("Repo2Image"))
				{
					Credentials = new Credentials("")
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

				Image<Rgba32> iconImage = null;
				Rgb[] vibrantColours = null;
				try
				{
					using var imageStream = await HttpClient.GetStreamAsync($"https://github.com/{repo.Owner.Login}/{repo.Name}/raw/main/images/icon.png");
					iconImage = await Image.LoadAsync<Rgba32>(imageStream);
					vibrantColours = iconImage.GetVibrantColours();
				}
				catch (Exception ex)
				{

				}

				using var image = new Image<Rgba32>(420, 80);
				image.Mutate(x =>
				{
					if (vibrantColours is not null)
					{
						var colouredAreaGradient = new LinearGradientBrush(
							new PointF(0, 0),
							new PointF(image.Width, 0),
							GradientRepetitionMode.None,
							ImageColourSampler.GetColourStops(vibrantColours)
						);
						x.Fill(colouredAreaGradient);
						x.Fill(Color.FromRgba(0, 0, 0, (byte)(255 * 0.2)));
					}
					else
					{
						x.Fill(Color.ParseHex("#605858"));
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
				return new FileStreamResult(stream, "image/png")
				{
					EntityTag = new EntityTagHeaderValue(new StringSegment($"\"{owner}-{repoName}-{repo.StargazersCount}-{repo.ForksCount}\"")),
					LastModified = DateTime.UtcNow.Date
				};
			}
			catch (Exception ex)
			{
				return new BadRequestObjectResult(ex.Message);
				//return new BadRequestObjectResult("Invalid owner/repo");
			}
		}

		private string GetNumberString(int number)
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
