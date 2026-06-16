using System.Drawing;
using Windows.Agent.Interface;

namespace Windows.Agent.Test.OCR
{
    internal sealed class FakeOcrService : IOcrService
    {
        public Task<(string Text, int Status)> ExtractTextFromRegionAsync(int x, int y, int width, int height, CancellationToken cancellationToken = default)
            => Task.FromResult<(string Text, int Status)>(("FAKE_REGION_TEXT", 0));

        public Task<(string Text, int Status)> ExtractTextFromScreenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<(string Text, int Status)>(("FAKE_SCREEN_TEXT", 0));

        public Task<(bool Found, int Status)> FindTextOnScreenAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult<(bool Found, int Status)>((false, 0));

        public Task<(Point? Coordinates, int Status)> GetTextCoordinatesAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult<(Point? Coordinates, int Status)>((null, 0));

        public Task<(string Text, int Status)> ExtractTextFromImageAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(imageStream);
            return Task.FromResult<(string Text, int Status)>(("FAKE_IMAGE_TEXT", 0));
        }
    }
}
