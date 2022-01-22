using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using System.Collections.Generic;
using System.Linq;

namespace Repo2Image;

internal static class ImageColourSampler
{
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

		if (colours.Length > 2)
		{
			colourStops.Add(new ColorStop(0, GetColour(colours[0])));
			colourStops.Add(new ColorStop(0.5f, GetColour(colours[colours.Length / 2])));
			colourStops.Add(new ColorStop(1, GetColour(colours[^1])));
		}
		else if (colours.Length > 1)
		{
			colourStops.Add(new ColorStop(0, GetColour(colours[0])));
			colourStops.Add(new ColorStop(1, GetColour(colours[^1])));
		}
		else
		{
			colourStops.Add(new ColorStop(0, GetColour(colours[0])));
		}

		return colourStops.ToArray();
	}

	private static Color GetColour(Rgb value)
	{
		return Color.FromRgb(
			(byte)(value.R * 255),
			(byte)(value.G * 255),
			(byte)(value.B * 255)
		);
	}
}
