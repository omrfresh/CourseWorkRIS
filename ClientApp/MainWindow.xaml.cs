using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClientApp
{
    public partial class MainWindow : Window
    {
        private readonly UdpClientHandler client;
        private List<string> selectedImagePaths = new List<string>();
        private readonly List<ProcessedImageResult> processedResults = new List<ProcessedImageResult>();
        private int _currentImageIndex = -1;

        public MainWindow()
        {
            InitializeComponent();
            AppLogger.Initialize("Client");
            client = new UdpClientHandler("127.0.0.1", 8080);
            client.StartListening(UpdateProgress);
        }

        /// <summary>
        /// Проверяет все поля ввода на корректность и возвращает параметры.
        /// </summary>
        /// <returns>True, если все параметры верны, иначе false.</returns>
        private bool ValidateAndGetParameters(out int diameter, out double sigmaColor, out double sigmaSpace, out int threadCount)
        {
            diameter = 0;
            sigmaColor = 0;
            sigmaSpace = 0;
            threadCount = 0;

            if (!int.TryParse(txtDiameter.Text, out diameter) || diameter <= 0)
            {
                MessageBox.Show("Параметр 'Diameter' должен быть целым положительным числом.", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (diameter % 2 == 0)
            {
                MessageBox.Show("Параметр 'Diameter' должен быть нечётным числом (например, 3, 5, 9).", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!double.TryParse(txtSigmaColor.Text, out sigmaColor) || sigmaColor <= 0)
            {
                MessageBox.Show("Параметр 'Sigma Color' должен быть положительным числом.", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!double.TryParse(txtSigmaSpace.Text, out sigmaSpace) || sigmaSpace <= 0)
            {
                MessageBox.Show("Параметр 'Sigma Space' должен быть положительным числом.", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(txtThreadCount.Text, out threadCount) || threadCount <= 0)
            {
                MessageBox.Show("Параметр 'Количество потоков' должен быть целым положительным числом.", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void btnSelectImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                selectedImagePaths = openFileDialog.FileNames.ToList();
                AppLogger.Log($"[Client] Выбрано {selectedImagePaths.Count} изображение(й).");
                txtSelectedImage.Text = $"{selectedImagePaths.Count} изображение(й) выбрано";
                _currentImageIndex = selectedImagePaths.Any() ? 0 : -1;

                processedResults.Clear();
                imgProcessed.Source = null;
                pnlNavigation.Visibility = Visibility.Collapsed;
                txtTotalTime.Text = "";
                UpdateDisplayedImage();

                btnStartProcessing.IsEnabled = selectedImagePaths.Any();
            }
        }

        private async void btnStartProcessing_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedImagePaths.Any())
            {
                MessageBox.Show("Пожалуйста, выберите одно или несколько изображений.");
                return;
            }

            if (!ValidateAndGetParameters(out int diameter, out double sigmaColor, out double sigmaSpace, out int threadCount))
            {
                return;
            }

            btnStartProcessing.IsEnabled = false;
            btnSelectImage.IsEnabled = false;
            processedResults.Clear();
            pnlNavigation.Visibility = Visibility.Collapsed;
            txtTotalTime.Text = "Обработка...";

            string modeStr = rbSingleThread.IsChecked == true ? "Однопоточный" : "Многопоточный";
            AppLogger.Log($"[Client] Начало обработки. Режим: {modeStr}, Потоков: {threadCount}, Diameter: {diameter}");

            var stopwatch = Stopwatch.StartNew();

            foreach (var path in selectedImagePaths)
            {
                try
                {
                    progressBar.Value = 0;
                    txtSelectedImage.Text = $"Обработка: {Path.GetFileName(path)}...";
                    AppLogger.Log($"[Client] Отправка файла: {Path.GetFileName(path)}");

                    byte[] imageBytes = File.ReadAllBytes(path);
                    using (var ms = new MemoryStream())
                    using (var writer = new BinaryWriter(ms))
                    {
                        writer.Write(rbSingleThread.IsChecked == true ? 0 : 1);
                        writer.Write(threadCount);
                        writer.Write(diameter);
                        writer.Write(sigmaColor);
                        writer.Write(sigmaSpace);
                        writer.Write(imageBytes.Length);
                        writer.Write(imageBytes);
                        await client.SendDataAsync(ms.ToArray());
                    }

                    byte[] resultData = await client.ReceiveImageAsync();
                    AppLogger.Log($"[Client] Получен результат для файла: {Path.GetFileName(path)}");
                    DisplayProcessedImage(path, resultData);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[Client] КРИТИЧЕСКАЯ ОШИБКА при обработке {Path.GetFileName(path)}: {ex.Message}");
                    MessageBox.Show($"Произошла ошибка при обработке {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            stopwatch.Stop();
            txtTotalTime.Text = $"Общее время обработки: {stopwatch.Elapsed:g}";
            AppLogger.Log($"[Client] Обработка завершена. Общее время: {stopwatch.Elapsed:g}");

            if (processedResults.Any())
            {
                _currentImageIndex = 0;
                UpdateDisplayedImage();
                pnlNavigation.Visibility = Visibility.Visible;
            }

            txtSelectedImage.Text = $"{selectedImagePaths.Count} изображение(й) обработано.";
            btnStartProcessing.IsEnabled = true;
            btnSelectImage.IsEnabled = true;
        }

        private void UpdateProgress(int progress)
        {
            Dispatcher.BeginInvoke(() => progressBar.Value = progress);
        }

        private void DisplayProcessedImage(string originalPath, byte[] imageData)
        {
            Dispatcher.BeginInvoke(() =>
            {
                BitmapImage bitmapImage = new BitmapImage();
                using (var ms = new MemoryStream(imageData))
                {
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                }

                processedResults.Add(new ProcessedImageResult
                {
                    FileName = Path.GetFileName(originalPath),
                    ProcessedSource = bitmapImage
                });

                _currentImageIndex = processedResults.Count - 1;
                UpdateDisplayedImage();
            });
        }

        private void rbMode_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsInitialized)
            {
                txtThreadCount.IsEnabled = rbMultiThread.IsChecked == true;
            }
        }

        private void btnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = AppLogger.LogFilePath;
                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Лог-файл еще не создан.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть лог-файл: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageIndex > 0)
            {
                _currentImageIndex--;
                UpdateDisplayedImage();
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageIndex < selectedImagePaths.Count - 1)
            {
                _currentImageIndex++;
                UpdateDisplayedImage();
            }
        }

        private void UpdateDisplayedImage()
        {
            if (_currentImageIndex < 0 || _currentImageIndex >= selectedImagePaths.Count)
            {
                imgOriginal.Source = null;
                imgProcessed.Source = null;
                txtImageCounter.Text = "";
                return;
            }

            var originalPath = selectedImagePaths[_currentImageIndex];
            imgOriginal.Source = new BitmapImage(new Uri(originalPath));

            if (_currentImageIndex < processedResults.Count)
            {
                imgProcessed.Source = processedResults[_currentImageIndex].ProcessedSource;
            }
            else
            {
                imgProcessed.Source = null;
            }

            txtImageCounter.Text = $"{_currentImageIndex + 1} / {selectedImagePaths.Count}";

            btnPrevious.IsEnabled = _currentImageIndex > 0;
            btnNext.IsEnabled = _currentImageIndex < selectedImagePaths.Count - 1;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            client.Close();
        }
    }
}