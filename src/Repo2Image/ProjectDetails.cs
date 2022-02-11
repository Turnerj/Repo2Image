using SixLabors.ImageSharp.ColorSpaces;
using Statiq.Common;

namespace Repo2Image;

internal record struct ProjectDetails(
	string Owner, 
	string RepositoryName, 
	int NumberOfForks, 
	int NumberOfStargazers,
	long NumberOfDownloads,
	Rgb[] VibrantColours
);