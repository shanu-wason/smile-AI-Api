namespace SmileApi.Application.Interfaces;

public interface IImageProcessingService
{
    Task<(byte[] ProcessedImage, double ImageQualityScore)> ProcessImageAsync(string imageUrl);
}
