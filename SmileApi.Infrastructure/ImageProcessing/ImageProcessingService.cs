using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SmileApi.Application.Interfaces;

namespace SmileApi.Infrastructure.ImageProcessing;

public class ImageProcessingService : IImageProcessingService
{
    private const int MaxFileSizeInBytes = 20 * 1024 * 1024;
    private const int MaxImageWidth = 1024;
    private readonly HttpClient _httpClient;

    public ImageProcessingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(byte[] ProcessedImage, double ImageQualityScore)> ProcessImageAsync(string imageUrl)
    {
        byte[] imageBytes;
        try
        {
            imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new ArgumentException("Failed to download image from the provided URL.", ex);
        }

        if (imageBytes.Length > MaxFileSizeInBytes)
            throw new ArgumentException("Image exceeds the maximum allowed size of 20MB.");

        using var memoryStream = new MemoryStream(imageBytes);
        using var image = await Image.LoadAsync<Rgba32>(memoryStream);

        var format = await Image.DetectFormatAsync(new MemoryStream(imageBytes));
        var allowedFormats = new[] { "JPEG", "PNG", "WEBP", "GIF", "BMP", "TIFF" };
        if (format == null || !allowedFormats.Contains(format.Name.ToUpperInvariant()))
            throw new ArgumentException($"Unsupported image format: {format?.Name ?? "Unknown"}. Only JPEG and PNG are allowed.");

        var resolutionScore = CalculateResolutionScore(image.Width);
        var brightnessScore = CalculateBrightnessScore(image);
        var blurScore = CalculateBlurScore(image);
        var contrastScore = CalculateContrastScore(image);
        var finalImageQualityScore = (0.25 * resolutionScore) + (0.25 * brightnessScore) + (0.30 * blurScore) + (0.20 * contrastScore);
        finalImageQualityScore = Math.Clamp(finalImageQualityScore, 0.0, 1.0);

        if (image.Width > MaxImageWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaxImageWidth, 0),
                Mode = ResizeMode.Max
            }));
        }

        image.Metadata.ExifProfile = null;

        using var outputStream = new MemoryStream();
        await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = 90 });

        return (outputStream.ToArray(), finalImageQualityScore);
    }

    private static double CalculateResolutionScore(int width)
    {
        if (width >= 1024) return 1.0;
        if (width >= 800) return 0.8;
        if (width >= 512) return 0.6;
        if (width >= 200) return 0.4;
        return 0.2;
    }

    private static double CalculateBrightnessScore(Image<Rgba32> image)
    {
        double totalIntensity = 0;
        int pixelCount = image.Width * image.Height;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
                foreach (ref Rgba32 pixel in pixelRow)
                {
                    double luminance = 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
                    totalIntensity += luminance;
                }
            }
        });
        double avgIntensity = totalIntensity / pixelCount;
        if (avgIntensity >= 90 && avgIntensity <= 170) return 1.0;
        if ((avgIntensity >= 70 && avgIntensity < 90) || (avgIntensity > 170 && avgIntensity <= 200)) return 0.7;
        return 0.4;
    }

    private static double CalculateBlurScore(Image<Rgba32> image)
    {
        double sum = 0, sumSq = 0;
        int validPixels = 0;
        double[,] luminance = new double[image.Width, image.Height];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    Rgba32 p = pixelRow[x];
                    luminance[x, y] = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
                }
            }
        });
        for (int y = 1; y < image.Height - 1; y++)
        {
            for (int x = 1; x < image.Width - 1; x++)
            {
                double laplacianValue = luminance[x, y - 1] + luminance[x - 1, y] - (4 * luminance[x, y]) + luminance[x + 1, y] + luminance[x, y + 1];
                sum += laplacianValue;
                sumSq += laplacianValue * laplacianValue;
                validPixels++;
            }
        }
        if (validPixels == 0) return 0.4;
        double mean = sum / validPixels;
        double variance = (sumSq / validPixels) - (mean * mean);
        if (variance > 200) return 1.0;
        if (variance >= 100) return 0.7;
        return 0.4;
    }

    private static double CalculateContrastScore(Image<Rgba32> image)
    {
        double sum = 0, sumSq = 0;
        int pixelCount = image.Width * image.Height;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
                foreach (ref Rgba32 pixel in pixelRow)
                {
                    double luminance = 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
                    sum += luminance;
                    sumSq += luminance * luminance;
                }
            }
        });
        double mean = sum / pixelCount;
        double variance = (sumSq / pixelCount) - (mean * mean);
        double stdDev = Math.Sqrt(Math.Max(0, variance));
        if (stdDev >= 50) return 1.0;
        if (stdDev >= 30) return 0.7;
        return 0.4;
    }
}
