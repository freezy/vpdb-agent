﻿using System.IO;
using System.Linq;
using FluentAssertions;
using ReactiveUI;
using Splat;
using VpdbAgent.Data.Objects;
using VpdbAgent.PinballX;
using VpdbAgent.PinballX.Models;
using Xunit;
using Xunit.Abstractions;

namespace VpdbAgent.Tests
{
	public class MenuManager : BaseTest
	{
		public MenuManager(ITestOutputHelper outputHelper) : base(outputHelper) { }

		[Fact]
		public void ShouldReadInitialSystem()
		{
			// setup
			var env = new TestEnvironment(Logger);
			env.MarshallManager.Setup(f => f.ParseIni(TestEnvironment.PinballXIniPath)).Returns(TestEnvironment.GetPinballXIni(Ini1));
			var menuManager = env.Locator.GetService<IMenuManager>();

			// test 
			menuManager.Initialize();

			// assert
			menuManager.Systems.ToList().Should()
				.NotBeEmpty()
				.And.HaveCount(1);
			var system = menuManager.Systems[0];
			system.Name.Should().Be("Visual Pinball");
			system.Executable.Should().Be("VPinball.exe");
			system.Enabled.Should().Be(true);
			system.WorkingPath.Should().Be(@"C:\Visual Pinball");
			system.Parameters.Should().Be("/play - \"[TABLEPATH]\\[TABLEFILE]\"");
		}

		[Fact]
		public void ShouldReadInitialSystems()
		{
			// setup
			var env = new TestEnvironment(Logger);
			env.MarshallManager.Setup(f => f.ParseIni(TestEnvironment.PinballXIniPath)).Returns(TestEnvironment.GetPinballXIni(Ini3));
			var menuManager = env.Locator.GetService<IMenuManager>();

			// test
			menuManager.Initialize();

			// assert
			menuManager.Systems.ToList().Should()
				.NotBeEmpty()
				.And.HaveCount(3);
			menuManager.Systems.FirstOrDefault(s => s.Name == "Visual Pinball").Should().NotBeNull();
			menuManager.Systems.FirstOrDefault(s => s.Name == "Future Pinball").Should().NotBeNull();
			menuManager.Systems.FirstOrDefault(s => s.Name == "MAME").Should().NotBeNull();
		}

		[Fact]
		public void ShouldUpdateSystems()
		{
			// setup
			var env = new TestEnvironment(Logger);
			env.MarshallManager.Setup(f => f.ParseIni(TestEnvironment.PinballXIniPath)).Returns(TestEnvironment.GetPinballXIni(Ini1));
			var menuManager = env.Locator.GetService<IMenuManager>();

			// test
			menuManager.Initialize();

			// now, pinballx.ini has changed.
			env.MarshallManager.Setup(f => f.ParseIni(TestEnvironment.PinballXIniPath)).Returns(TestEnvironment.GetPinballXIni(Ini3));
			env.PinballXIniWatcher.OnNext(TestEnvironment.PinballXIniPath);

			// assert
			menuManager.Systems.ToList().Should()
				.NotBeEmpty()
				.And.HaveCount(3);
		}

		[Fact]
		public void ShouldParseGames()
		{
			// setup
			var env = new TestEnvironment(Logger);
			var menu = new PinballXMenu();
			menu.Games.Add(new PinballXGame() {
				Filename = "Test_Game",
				Description = "Test Game (Test 2016)"
			});

			env.Directory.Setup(d => d.Exists(TestEnvironment.VisualPinballDatabasePath)).Returns(true);
			env.Directory.Setup(d => d.GetFiles(TestEnvironment.VisualPinballDatabasePath)).Returns(new []{ Path.GetFileName(TestEnvironment.VisualPinballDatabaseXmlPath) });
			env.MarshallManager.Setup(m => m.UnmarshallXml("Visual Pinball.xml")).Returns(menu);

			var menuManager = env.Locator.GetService<IMenuManager>();

			// test
			menuManager.Initialize();

			// assert
			menuManager.Systems.ToList().Should().NotBeEmpty().And.HaveCount(1);
			var system = menuManager.Systems[0];
			system.Games.Should().NotBeEmpty().And.HaveCount(1);
			//system.Games[0].Filename.Should().Be("Test_Game");
			//system.Games[0].Description.Should().Be("Test Game (Test 2016)");
			//system.Games[0].DatabaseFile.Should().Be(Path.GetFileName(TestEnvironment.VisualPinballDatabaseXmlPath));
		}

		private static readonly string[] Ini1 = {
			@"[VisualPinball]",
			@"Enabled = true",
			@"WorkingPath = C:\Visual Pinball",
			@"Executable = VPinball.exe",
			"Parameters = /play - \"[TABLEPATH]\\[TABLEFILE]\"",
		};

		private static readonly string[] Ini3 = {
			@"[VisualPinball]",
			@"Enabled = true",
			@"WorkingPath = C:\Visual Pinball",
			@"TablePath = C:\Visual Pinball\Tables",
			@"Executable = VPinball.exe",
			"Parameters = /play - \"[TABLEPATH]\\[TABLEFILE]\"",

			@"[FuturePinball]",
			@"Enabled = true",
			@"WorkingPath = C:\Future Pinball",
			@"TablePath = C:\Future Pinball\Tables",
			@"Executable = FuturePinball.exe",
			"Parameters = /open \"[TABLEPATH]\\[TABLEFILE]\" /play /exit /arcaderender",

			@"[System_1]",
			@"Name = MAME",
			@"Enabled = true",
			@"WorkingPath = C:\Emulators\MAME",
			@"TablePath = C:\Emulators\MAME\Tables",
			@"Executable = mamep64.exe",
			"Parameters =[TABLEFILE]",
		};
	}

}