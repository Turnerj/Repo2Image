using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using System.Net.Http;

namespace Repo2Image
{
	public class SampleColours
	{
		private readonly HttpClient HttpClient;

		public SampleColours(HttpClient httpClient)
		{
			HttpClient = httpClient;
		}

		[FunctionName("SampleColours")]
		public async Task<IActionResult> Run(
			[HttpTrigger(AuthorizationLevel.Anonymous, Route = "{owner}/{repoName}/sample")] HttpRequest req,
			string owner,
			string repoName
		)
		{
			try
			{
				Image<Rgb24> iconImage = null;
				using var imageStream = await HttpClient.GetStreamAsync($"https://github.com/{owner}/{repoName}/raw/main/images/icon.png");
				iconImage = await Image.LoadAsync<Rgb24>(imageStream);

				var vibrantColours = iconImage.GetVibrantColours();
				var gradient = new LinearGradientBrush(
					new PointF(0, 200),
					new PointF(200, 200),
					GradientRepetitionMode.None,
					ImageColourSampler.GetColourStops(vibrantColours)
				);
				var outputImage = new Image<Rgba32>(200, 220);
				outputImage.Mutate(x =>
				{
					x.DrawImage(iconImage, 1f);
					x.Fill(gradient, new RectangleF(0, 200, 200, 20));
				});

				var stream2 = new MemoryStream();
				await outputImage.SaveAsync(stream2, new PngEncoder());
				stream2.Seek(0, SeekOrigin.Begin);
				return new FileStreamResult(stream2, "image/png");
			}
			catch (Exception ex)
			{
				return new BadRequestObjectResult(ex.Message);
			}
		}
	}
}
