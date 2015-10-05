﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NLog;
using Splat;
using VpdbAgent.Vpdb;

namespace VpdbAgent.Common
{
	public class ImageUtils
	{
		private static ImageUtils _instance;

		// dependencies
		private readonly IVpdbClient _vpdbClient = Locator.Current.GetService<IVpdbClient>();
		private readonly Logger _logger = Locator.Current.GetService<Logger>();

		private ImageUtils() { }

		public void LoadImage(string path, System.Windows.Controls.Image imageView, Dispatcher dispatcher)
		{
			if (IsCached(path)) {
				imageView.Source = new BitmapImage(new Uri(GetLocalPath(path)));
				return;
			}
			imageView.Opacity = 0;
			imageView.Source = null;
			var webRequest = _vpdbClient.GetWebRequest(path);
			webRequest.BeginGetResponse((ar) =>
			{
				try {
					var response = webRequest.EndGetResponse(ar);
					var stream = response.GetResponseStream();
					if (stream != null && !stream.CanRead) {
						return;
					}
					var buffer = new byte[response.ContentLength];
					stream?.BeginRead(buffer, 0, buffer.Length, (aResult) =>
					{
						stream.EndRead(aResult);
						Cache(path, buffer);
						var image = new BitmapImage();
						image.BeginInit();
						image.StreamSource = new MemoryStream(buffer);
						image.EndInit();
						image.Freeze();
						dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate {
							var da = new DoubleAnimation {
								From = 0,
								To = 1,
								Duration = new Duration(TimeSpan.FromMilliseconds(200))
							};
							imageView.Source = image;
							imageView.BeginAnimation(UIElement.OpacityProperty, da);
						}));
					}, null);
				} catch (Exception e) {
					_logger.Warn("Error loading image {0}: {1}", webRequest.RequestUri.AbsoluteUri, e.Message);
				}
			}, null);
		}

		private static bool IsCached(string path)
		{
			return File.Exists(GetLocalPath(path));
		}

		private static string GetLocalPath(string path)
		{
			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"VpdbAgent", 
				"Cache", 
				path.Replace("/", @"\").Substring(1)
			);
		}

		private static void Cache(string path, byte[] bytes)
		{
			string localPath = GetLocalPath(path);
			if (!Directory.Exists(Path.GetDirectoryName(localPath))) {
				Directory.CreateDirectory(Path.GetDirectoryName(localPath));
			}
			File.WriteAllBytes(localPath, bytes);
		}

		public static ImageUtils GetInstance()
		{
			return _instance ?? (_instance = new ImageUtils());
		}
	}
}
