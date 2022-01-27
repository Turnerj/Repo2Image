using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using System.Collections.Generic;
using System.Linq;
using TurnerSoftware.Vibrancy;

namespace Repo2Image;

internal static class ImageColourSampler
{
	private static readonly Palette Palette = new(new PaletteOptions(new SwatchDefinition[]
	{
		SwatchDefinition.Vibrant
	}));

	public static Rgb[] GetVibrantColours(this Image<Rgb24> image)
	{
		var swatches = Palette.GetSwatches(image);
		return swatches[0].GetColors()
			.Where(c => c.Hsv.S > 0.6f)
			.OrderBy(c => c.Hsv.H)
			.Select(c => c.Rgb)
			.ToArray();
	}

	public static Rgb[] GetVibrantColours(this Image<Rgba32> image)
	{
		var localImage = image.Clone();
		localImage.Mutate(x =>
		{
			var horizontalPadding = (int)(localImage.Width * 0.15);
			var verticalPadding = (int)(localImage.Height * 0.15);
			x.Crop(new Rectangle(horizontalPadding, verticalPadding, localImage.Width - (horizontalPadding * 2), localImage.Height - (verticalPadding * 2)));
			x.Resize(20, 0);
			x.Pixelate(5);
			x.Saturate(2f);
			x.Resize(4, 0, new NearestNeighborResampler());
		});

		var vibrantColours = new List<Hsv>();
		var colourSpaceConverter = new ColorSpaceConverter();
		for (var y = 0; y < localImage.Height; y++)
		{
			for (var x = 0; x < localImage.Width; x++)
			{
				var pixel = localImage[x, y];
				if (pixel.A < (255 * 0.7))
				{
					continue;
				}

				var hsv = colourSpaceConverter.ToHsv(pixel);
				if (hsv.S < 0.6f || hsv.V < 0.6f)
				{
					continue;
				}
				vibrantColours.Add(hsv);
			}
		}

		return vibrantColours.OrderBy(c => c.H)
			.ThenByDescending(c => c.S + c.V)
			.Select(c => colourSpaceConverter.ToRgb(c))
			.ToArray();
	}

	public static ColorStop[] GetColourStops(Rgb[] colours)
	{
		var colourStops = new List<ColorStop>();

		var stopDistance = 1f / (colours.Length - 1);
		for (var i = 0; i < colours.Length; i++)
		{
			var colour = colours[i];
			colourStops.Add(new ColorStop(
				stopDistance * i,
				Color.FromRgb(
					(byte)(colour.R * 255),
					(byte)(colour.G * 255),
					(byte)(colour.B * 255)
				)
			));
		}

		return colourStops.ToArray();
	}
}
