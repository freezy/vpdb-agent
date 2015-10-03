﻿using NLog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using VpdbAgent.Vpdb;
using PusherClient;
using System;
using System.Windows;
using System.Collections.Generic;
using ReactiveUI;
using VpdbAgent.ViewModels;

namespace VpdbAgent.Views
{
	/// <summary>
	/// Interaction logic for MainPage.xaml
	/// </summary>
	public partial class MainView : UserControl, IViewFor<MainViewModel>
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public MainView()
		{
			InitializeComponent();
			//TestPusher();

			this.WhenAnyValue(x => x.ViewModel).BindTo(this, x => x.DataContext);
			this.WhenActivated(d =>
			{
				d(this.OneWayBind(ViewModel, vm => vm.Platforms, v => v.PlatformList.ItemsSource));
				d(this.OneWayBind(ViewModel, vm => vm.Games, v => v.GameList.ItemsSource));
			});
		}

		#region ViewModel
		public MainViewModel ViewModel
		{
			get { return (MainViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}

		public static readonly DependencyProperty ViewModelProperty =
		   DependencyProperty.Register("ViewModel", typeof(MainViewModel), typeof(MainView), new PropertyMetadata(null));

		object IViewFor.ViewModel
		{
			get { return ViewModel; }
			set { ViewModel = (MainViewModel)value; }
		} 
		#endregion

		#region Pusher

		private void TestPusher()
		{
			// pusher test
			Logger.Info("Setting up pusher...");
			var client = VpdbClient.GetInstance();

			client.Pusher.ConnectionStateChanged += PusherConnectionStateChanged;
			client.Pusher.Error += PusherError;

			var testChannel = client.Pusher.Subscribe("test-channel");
			testChannel.Subscribed += PusherSubscribed;

			// inline binding
			testChannel.Bind("test-message", (dynamic data) =>
			{
				Logger.Info("[{0}]: {1}", data.name, data.message);
			});

			client.Pusher.Connect();
		}

		private static void PusherConnectionStateChanged(object sender, ConnectionState state)
		{
			Logger.Info("Pusher connection {0}", state);
		}

		private static void PusherError(object sender, PusherException error)
		{
			Logger.Error(error, "Pusher error!");
		}

		private static void PusherSubscribed(object sender)
		{
			Logger.Info("Subscribed to channel.");
		}
		#endregion
		
		/*
		private async void getReleases()
		{

			VpdbClient client = new VpdbClient();

			try {
				Releases = await client.Api.GetReleases();
				GamesList.ItemsSource = Releases;
				foreach (Release release in Releases) {
					Console.WriteLine("{0} - {1} ({2})", release.Game.Title, release.Name, release.Id);
				}
			} catch (Exception e) {
				Console.WriteLine("Error retrieving releases: {0}", e.Message);
			}
		}*/
	}
}