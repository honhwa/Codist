﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AppHelpers;
using Codist.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using R = Codist.Properties.Resources;
using Task = System.Threading.Tasks.Task;

namespace Codist.Display
{
	static class ResourceMonitor
	{
		static Timer _Timer;
		static readonly StackPanel _MeterContainer = new StackPanel {
			Orientation = Orientation.Horizontal,
			Children = { new ContentPresenter(), new ContentPresenter(), new ContentPresenter(), new ContentPresenter() }
		};
		static Meter _CpuMeter, _RamMeter, _DriveMeter, _NetworkMeter;
		static int _IsInited;
		static CancellationTokenSource _CancellationTokenSource;

		public static void Reload(DisplayOptimizations option) {
			if (option.HasAnyFlag(DisplayOptimizations.ResourceMonitors) == false) {
				Stop();
				return;
			}
			ToggleMeter<CpuMeter>(0, option, DisplayOptimizations.ShowCpu, ref _CpuMeter);
			ToggleMeter<DriveMeter>(1, option, DisplayOptimizations.ShowDrive, ref _DriveMeter);
			ToggleMeter<RamMeter>(2, option, DisplayOptimizations.ShowMemory, ref _RamMeter);
			ToggleMeter<NetworkMeter>(3, option, DisplayOptimizations.ShowNetwork, ref _NetworkMeter);
			if (_Timer == null) {
				_Timer = new Timer(Update, null, 1000, 1000);
			}
		}

		static void ToggleMeter<TMeter>(int index, DisplayOptimizations option, DisplayOptimizations flag, ref Meter meter) where TMeter : Meter, new() {
			if (option.MatchFlags(flag)) {
				if (meter != null) {
					meter.Start();
				}
				else {
					meter = new TMeter();
					_MeterContainer.Children.RemoveAt(index);
					_MeterContainer.Children.Insert(index, meter);
				}
			}
			else {
				meter?.Stop();
			}
		}

		static void Stop() {
			if (_Timer != null) {
				_Timer.Dispose();
				_Timer = null;
				_CpuMeter?.Stop();
				_RamMeter?.Stop();
				_DriveMeter?.Stop();
				_NetworkMeter?.Stop();
			}
		}

		static void Update(object dummy) {
			UpdateAsync(SyncHelper.CancelAndRetainToken(ref _CancellationTokenSource)).FireAndForget();
		}

		async static Task UpdateAsync(CancellationToken cancellationToken) {
			_CpuMeter?.Sample();
			_RamMeter?.Sample();
			_DriveMeter?.Sample();
			_NetworkMeter?.Sample();
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			if (_IsInited == 0) {
				Init();
				return;
			}
			_CpuMeter?.Update();
			_RamMeter?.Update();
			_DriveMeter?.Update();
			_NetworkMeter?.Update();
		}

		static void Init() {
			var statusPanel = Application.Current.MainWindow.GetFirstVisualChild<Panel>(i => i.Name == "StatusBarPanel");
			if (statusPanel == null) {
				return;
			}
			if (Interlocked.CompareExchange(ref _IsInited, 1, 0) == 0) {
				_IsInited = 1;
				statusPanel.Children.Insert(0, _MeterContainer);
				_MeterContainer.MouseLeftButtonUp += StartTaskMgr;
			}
		}

		static void StartTaskMgr(object sender, System.Windows.Input.MouseButtonEventArgs e) {
			try {
				Process.Start("TaskMgr.exe");
			}
			catch (Exception ex) {
				Debug.WriteLine("Failed to start task manager: " + ex.ToString());
			}
		}

		abstract class Meter : StackPanel
		{
			readonly TextBlock _Label;

			protected Meter(int iconId, string tooltip) {
				Orientation = Orientation.Horizontal;
				Children.Add(ThemeHelper.GetImage(iconId).WrapMargin(WpfHelper.SmallHorizontalMargin));
				Children.Add(_Label = new TextBlock { MinWidth = 40, VerticalAlignment = VerticalAlignment.Center }.ReferenceProperty(Control.ForegroundProperty, EnvironmentColors.StatusBarDefaultTextBrushKey));
				ToolTip = new CommandToolTip(iconId, tooltip)
					.ReferenceCrispImageBackground(EnvironmentColors.ToolTipColorKey);
				this.SetTipPlacementTop();
			}

			protected TextBlock Label => _Label;

			public abstract void Sample();
			public abstract void Update();

			public virtual void Start() {
				Visibility = Visibility.Visible;
			}
			public virtual void Stop() {
				Visibility = Visibility.Collapsed;
			}
		}

		abstract class SinglePerformanceCounterMeter : Meter
		{
			PerformanceCounter _Counter;
			float _Value;

			protected SinglePerformanceCounterMeter(int iconId, string tooltip) : base(iconId, tooltip) {
				_Counter = CreateCounter();
			}

			protected abstract PerformanceCounter CreateCounter();
			protected virtual void UpdateSample(float counterValue) { }
			protected abstract void UpdateDisplay(float counterValue);

			public override void Start() {
				base.Start();
				if (_Counter == null) {
					_Counter = CreateCounter();
				}
			}

			public override void Stop() {
				base.Stop();
				if (_Counter != null) {
					_Counter.Dispose();
					_Counter = null;
				}
			}

			public override void Sample() {
				var c = _Counter;
				if (c != null) {
					UpdateSample(_Value = c.NextValue());
				}
			}

			public override void Update() {
				try {
					UpdateDisplay(_Value);
				}
				catch (Exception ex) {
					Debug.WriteLine(ex);
				}
			}
		}

		abstract class MultiPerformanceCounterMeter : Meter
		{
			PerformanceCounter[] _Counters;
			float[] _Values;

			protected MultiPerformanceCounterMeter(int iconId, string tooltip) : base(iconId, tooltip) {
				_Counters = CreateCounters();
				_Values = new float[_Counters.Length];
			}

			protected abstract PerformanceCounter[] CreateCounters();
			protected virtual void UpdateSample(float[] counterValues) { }
			protected abstract void UpdateDisplay(float[] counterValues);

			public override void Start() {
				base.Start();
				if (_Counters == null) {
					_Counters = CreateCounters();
				}
			}

			public override void Stop() {
				base.Stop();
				if (_Counters != null) {
					foreach (var item in _Counters) {
						item.Dispose();
					}
					_Counters = null;
				}
			}

			public override void Sample() {
				var c = _Counters;
				if (c != null) {
					for (int i = 0; i < _Counters.Length; i++) {
						_Values[i] = _Counters[i].NextValue();
					}
					UpdateSample(_Values);
				}
			}

			public override void Update() {
				try {
					UpdateDisplay(_Values);
				}
				catch (Exception ex) {
					Debug.WriteLine(ex);
				}
			}
		}

		sealed class CpuMeter : SinglePerformanceCounterMeter
		{
			const int SampleCount = 10;
			readonly float[] _Samples = new float[SampleCount];
			float _SampleSum, _LastCounter;
			int _SampleIndex;

			public CpuMeter() : base(IconIds.Cpu, R.T_CpuUsage) {
			}

			protected override PerformanceCounter CreateCounter() {
				return new PerformanceCounter("Processor", "% Processor Time", "_Total");
			}

			protected override void UpdateSample(float counterValue) {
				_SampleSum -= _Samples[_SampleIndex];
				_Samples[_SampleIndex] = counterValue;
				_SampleSum += counterValue;
				if (++_SampleIndex == SampleCount) {
					_SampleIndex = 0;
				}
			}

			protected override void UpdateDisplay(float counterValue) {
				Label.Text = counterValue.ToString("0") + "%";
				Label.Opacity = (Math.Min(50, counterValue) + 50) / 100;
				counterValue = Math.Min(50, Math.Min(counterValue, _SampleSum / SampleCount)) / 50;
				if (counterValue < 0.2f) {
					if (_LastCounter >= 0.2f) {
						ClearValue(BackgroundProperty);
					}
				}
				else {
					Background = (counterValue < 0.4f ? Brushes.Yellow : counterValue < 0.6f ? Brushes.Orange : Brushes.Red).Alpha(counterValue);
				}
				_LastCounter = counterValue;
			}
		}

		sealed class RamMeter : SinglePerformanceCounterMeter
		{
			public RamMeter() : base(IconIds.Memory, R.T_MemoryUsage) {
			}

			protected override PerformanceCounter CreateCounter() {
				return new PerformanceCounter("Memory", "% Committed Bytes In Use");
			}

			protected override void UpdateDisplay(float counterValue) {
				Label.Text = counterValue.ToString("0") + "%";
				Label.Opacity = (counterValue + 100) / 200;
			}
		}

		sealed class DriveMeter : SinglePerformanceCounterMeter
		{
			const int SampleCount = 10;
			readonly float[] _Samples = new float[SampleCount];
			float _SampleSum, _LastCounter;
			int _SampleIndex;

			public DriveMeter() : base(IconIds.Drive, R.T_DriveUsage) {
			}

			protected override PerformanceCounter CreateCounter() {
				return new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total");
			}

			protected override void UpdateSample(float counterValue) {
				counterValue = (float)Math.Round(100 - counterValue, 0);
				_SampleSum -= _Samples[_SampleIndex];
				_Samples[_SampleIndex] = counterValue;
				_SampleSum += counterValue;
				if (++_SampleIndex == SampleCount) {
					_SampleIndex = 0;
				}
			}

			protected override void UpdateDisplay(float counterValue) {
				counterValue = counterValue > 100f ? 0f : (float)Math.Round(100 - counterValue, 0);
				Label.Text = counterValue.ToString("0") + "%";
				Label.Opacity = (Math.Min(50, counterValue) + 50) / 100;
				counterValue = Math.Min(30, Math.Min(counterValue, _SampleSum / SampleCount)) / 30;
				if (counterValue < 0.2f) {
					if (_LastCounter >= 0.2f) {
						ClearValue(BackgroundProperty);
					}
				}
				else {
					Background = (counterValue < 0.4f ? Brushes.Yellow : counterValue < 0.6f ? Brushes.Orange : Brushes.Red).Alpha(counterValue);
				}
				_LastCounter = counterValue;
			}
		}

		sealed class NetworkMeter : MultiPerformanceCounterMeter
		{
			const float MBit = 1024 * 1024, KBit = 1024;

			static readonly Comparer<(string, float)> __Comparer = Comparer<(string, float)>.Create((x, y) => y.Item2.CompareTo(x.Item2));
			static readonly string __0bps = "0" + R.T_Bps;
			bool _TooltipDisplayed;
			float _LastCounter;
			PerformanceCounter[] _Counters;

			public NetworkMeter() : base(IconIds.Network, R.T_NetworkUsage) {
				ToolTipService.SetShowDuration(this, Int32.MaxValue);
				Label.Opacity = 0.4;
			}

			protected override PerformanceCounter[] CreateCounters() {
				var cc = new PerformanceCounterCategory("Network Interface");
				var names = cc.GetInstanceNames();
				var pc = new PerformanceCounter[names.Length];
				for (int i = 0; i < pc.Length; i++) {
					pc[i] = cc.GetCounters(names[i])[0];
				}
				return _Counters = pc;
			}

			protected override void UpdateDisplay(float[] counterValues) {
				var v = counterValues.Sum();
				Label.Text = FlowToReading(v);
				if (v > 30 * KBit) {
					Label.Opacity = v > MBit ? 0.8 : 0.6;
				}
				else if (_LastCounter > 30 * KBit) {
					Label.Opacity = 0.4;
				}
				_LastCounter = v;
				if (_TooltipDisplayed && (ToolTip as CommandToolTip)?.Description is TextBlock t) {
					ShowToolTip(counterValues, t);
				}
			}

			void ShowToolTip(float[] counterValues, TextBlock t) {
				if (counterValues.Length == 1) {
					t.Text = _Counters[0].InstanceName + ": " + FlowToReading(counterValues[0]) + R.T_Bps;
					return;
				}
				ShowMultiValuesOnToolTip(counterValues, t);
			}

			void ShowMultiValuesOnToolTip(float[] counterValues, TextBlock t) {
				(string name, float val)[] cv = new (string, float)[counterValues.Length];
				for (int i = 0; i < cv.Length; i++) {
					var v = counterValues[i];
					cv[i] = v > 0 ? (_Counters[i].InstanceName, v) : default;
				}
				Array.Sort(cv, __Comparer);
				using (var r = Microsoft.VisualStudio.Utilities.ReusableStringBuilder.AcquireDefault(100)) {
					var sb = r.Resource;
					foreach (var (name, val) in cv) {
						if (val != 0) {
							if (sb.Length > 0) {
								sb.AppendLine();
							}
							sb.Append(name).Append(": ").Append(FlowToReading(val)).Append(R.T_Bps);
						}
					}
					t.Text = sb.Length == 0 ? __0bps : sb.ToString();
				}
			}

			static string FlowToReading(float v) {
				return v > MBit ? ((v / MBit).ToString("0.0") + "M")
					: v > KBit ? ((v / KBit).ToString("0.0") + "K")
					: v.ToString("0");
			}

			protected override void OnToolTipOpening(ToolTipEventArgs e) {
				base.OnToolTipOpening(e);
				_TooltipDisplayed = true;
			}

			protected override void OnToolTipClosing(ToolTipEventArgs e) {
				base.OnToolTipClosing(e);
				_TooltipDisplayed = false;
			}
		}
	}
}
