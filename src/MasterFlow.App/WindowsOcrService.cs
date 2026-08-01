using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace MasterFlow.App;

public sealed class WindowsOcrService
{
    public async Task<string> RecognizeAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("Выберите хотя бы один скриншот.", nameof(paths));
        }

        var engine = OcrEngine.TryCreateFromLanguage(new Language("ru-RU"))
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "В Windows не установлен язык распознавания текста. Добавьте русский язык в параметрах Windows и попробуйте снова.");
        var recognized = new List<string>();

        foreach (var path in paths)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth > OcrEngine.MaxImageDimension || decoder.PixelHeight > OcrEngine.MaxImageDimension)
            {
                throw new InvalidOperationException(
                    $"Скриншот «{Path.GetFileName(path)}» слишком большой для распознавания Windows.");
            }

            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                recognized.Add(result.Text.Trim());
            }
        }

        return recognized.Count == 0
            ? throw new InvalidOperationException(
                "Текст на скриншотах не найден. Выберите чёткие изображения без обрезанных сообщений.")
            : string.Join(Environment.NewLine + Environment.NewLine, recognized);
    }
}
