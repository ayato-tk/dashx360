using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace XboxMetroLauncher.Controls;

public sealed class MusicVisualizer : FrameworkElement
{
	private sealed class Particle
	{
		public double X;

		public double Y;

		public double Vx;

		public double Vy;

		public double Depth;

		public double Speed;

		public double Size;

		public double Alpha;

		public double ColorOffset;
	}

	private struct Shockwave
	{
		public double Age;

		public double Strength;

		public double HueOffset;

		public double Warp;
	}

	private readonly record struct SceneWeights(double Ribbon, double Tunnel, double Plasma, double Bloom, double Wave);

	public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register("IsActive", typeof(bool), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, new PropertyChangedCallback(OnIsActiveChanged)));

	public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register("Volume", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.7, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BassProperty = DependencyProperty.Register("Bass", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty MidProperty = DependencyProperty.Register("Mid", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrebleProperty = DependencyProperty.Register("Treble", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty LoudnessProperty = DependencyProperty.Register("Loudness", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty PeakProperty = DependencyProperty.Register("Peak", typeof(double), typeof(MusicVisualizer), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	private readonly Random _random = new Random(1978);

	private readonly Particle[] _particles;

	private readonly List<Shockwave> _shockwaves = new List<Shockwave>();

	private readonly Color[] _palette = new Color[4]
	{
		Color.FromRgb(120, 42, 214),
		Color.FromRgb(42, 78, 190),
		Color.FromRgb(24, 192, 224),
		Color.FromRgb(208, 48, 148)
	};

	private TimeSpan _lastFrame;

	private double _time;

	private double _bass;

	private double _mid;

	private double _treble;

	private double _energy;

	private double _burst;

	private double _cameraX;

	private double _cameraY;

	private double _cameraZoom = 1.0;

	private double _nextMutation;

	private double _mutation;

	private double _mutationTarget = 0.5;

	private double _lastProgress = -1.0;

	private double _impact;

	private double _dropEnergy;

	private double _sparkBurst;

	private double _lastLiveBass;

	private double _lastLiveLoudness;

	private double _lastKickTime = -10.0;

	private bool _isRenderingSubscribed;

	private static int _activeRendererCount;

	public static int ActiveRendererCount => Math.Max(0, _activeRendererCount);

	public bool IsActive
	{
		get
		{
			return (bool)((DependencyObject)this).GetValue(IsActiveProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(IsActiveProperty, (object)value);
		}
	}

	public double Volume
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(VolumeProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(VolumeProperty, (object)value);
		}
	}

	public double Progress
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(ProgressProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(ProgressProperty, (object)value);
		}
	}

	public double Bass
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(BassProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(BassProperty, (object)value);
		}
	}

	public double Mid
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(MidProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(MidProperty, (object)value);
		}
	}

	public double Treble
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(TrebleProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TrebleProperty, (object)value);
		}
	}

	public double Loudness
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(LoudnessProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(LoudnessProperty, (object)value);
		}
	}

	public double Peak
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(PeakProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(PeakProperty, (object)value);
		}
	}

	public MusicVisualizer()
	{
		base.ClipToBounds = true;
		base.SnapsToDevicePixels = false;
		_particles = new Particle[92];
		for (int i = 0; i < _particles.Length; i++)
		{
			_particles[i] = CreateParticle(i);
		}
		base.Loaded += delegate
		{
			UpdateRenderingSubscription();
		};
		base.Unloaded += delegate
		{
			UpdateRenderingSubscription();
		};
		base.IsVisibleChanged += (DependencyPropertyChangedEventHandler)delegate
		{
			UpdateRenderingSubscription();
		};
	}

	private static void OnIsActiveChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
	{
		if (dependencyObject is MusicVisualizer musicVisualizer)
		{
			musicVisualizer.UpdateRenderingSubscription();
		}
	}

	private void UpdateRenderingSubscription()
	{
		bool flag = base.IsLoaded && base.IsVisible && IsActive;
		if (flag == _isRenderingSubscribed)
		{
			return;
		}
		if (flag)
		{
			CompositionTarget.Rendering += OnRendering;
			Interlocked.Increment(ref _activeRendererCount);
		}
		else
		{
			CompositionTarget.Rendering -= OnRendering;
			if (_isRenderingSubscribed)
			{
				Interlocked.Decrement(ref _activeRendererCount);
			}
		}
		_isRenderingSubscribed = flag;
	}

	private void OnRendering(object? sender, EventArgs e)
	{
		if (e is RenderingEventArgs e2)
		{
			double num = ((_lastFrame == TimeSpan.Zero) ? (1.0 / 60.0) : Math.Clamp((e2.RenderingTime - _lastFrame).TotalSeconds, 0.001, 0.05));
			_lastFrame = e2.RenderingTime;
			_time += num;
			if (_time >= _nextMutation)
			{
				_mutationTarget = _random.NextDouble();
				_nextMutation = _time + 6.5 + _random.NextDouble() * 16.0;
			}
			_mutation = Lerp(_mutation, _mutationTarget, 1.0 - Math.Pow(0.07, num));
			double num2 = Math.Clamp(Volume, 0.0, 1.0);
			double num3 = ((_lastProgress < 0.0) ? 0.0 : Math.Abs(Progress - _lastProgress));
			_lastProgress = Progress;
			double num4 = (IsActive ? 1.0 : 0.28);
			double num5 = Math.Clamp(Bass, 0.0, 1.4);
			double num6 = Math.Clamp(Mid, 0.0, 1.3);
			double num7 = Math.Clamp(Treble, 0.0, 1.4);
			double num8 = Math.Clamp(Loudness, 0.0, 1.2);
			double num9 = Math.Clamp(Peak, 0.0, 1.5);
			bool flag = num5 + num6 + num7 + num8 > 0.025;
			double val = num5 - _lastLiveBass;
			double val2 = num8 - _lastLiveLoudness;
			double num10 = Math.Clamp(num9 * 1.05 + Math.Max(0.0, val) * 2.0 + Math.Max(0.0, val2) * 1.0 + Math.Max(0.0, num5 - 0.2) * 0.55, 0.0, 1.45);
			if (flag && num10 > 0.19 && _time - _lastKickTime > 0.12)
			{
				TriggerKick(num10);
			}
			_lastLiveBass = num5;
			_lastLiveLoudness = num8;
			double num11 = SmoothPulse(_time * 1.17 + Noise(_time * 0.09) * 4.0);
			double num12 = Noise01(_time * 0.23 + Progress * 0.013);
			double num13 = Noise01(_time * 1.61 + _mutation * 5.3);
			double num14 = num4 * (flag ? (0.04 + num5 * 0.98 + num9 * 0.42 + num8 * 0.15) : (0.35 + num2 * 0.45 + num11 * 0.45));
			double num15 = num4 * (flag ? (0.05 + num6 * 0.78 + num8 * 0.18) : (0.28 + num2 * 0.34 + num12 * 0.5));
			double num16 = num4 * (flag ? (0.04 + num7 * 0.88 + num9 * 0.16) : (0.2 + num2 * 0.28 + num13 * 0.62 + num3 * 0.02));
			_bass = Lerp(_bass, num14, (num14 > _bass) ? (1.0 - Math.Pow(1E-05, num)) : (1.0 - Math.Pow(0.012, num)));
			_mid = Lerp(_mid, num15, (num15 > _mid) ? (1.0 - Math.Pow(0.0004, num)) : (1.0 - Math.Pow(0.02, num)));
			_treble = Lerp(_treble, num16, (num16 > _treble) ? (1.0 - Math.Pow(0.0007, num)) : (1.0 - Math.Pow(0.045, num)));
			_energy = Lerp(_energy, Math.Clamp(_bass * 0.52 + _mid * 0.3 + _treble * 0.18 + num8 * 0.22, 0.0, 1.35), 1.0 - Math.Pow(0.002, num));
			double num17 = (flag ? Math.Clamp(num9 * 0.9 + Math.Pow(num5, 2.3) * 0.46, 0.0, 1.25) : Math.Pow(_bass, 3.2));
			_burst = Lerp(_burst, num17, (num17 > _burst) ? (1.0 - Math.Pow(1E-06, num)) : (1.0 - Math.Pow(0.035, num)));
			_impact = Lerp(_impact, Math.Clamp(num10 * 0.78 + _burst * 0.24, 0.0, 1.45), (num10 + _burst > _impact) ? (1.0 - Math.Pow(1E-06, num)) : (1.0 - Math.Pow(0.018, num)));
			_dropEnergy = Lerp(_dropEnergy, Math.Clamp(num8 * 0.86 + num5 * 0.28 + num6 * 0.16, 0.0, 1.18), 1.0 - Math.Pow((num8 > _dropEnergy) ? 0.001 : 0.04, num));
			_sparkBurst = Lerp(_sparkBurst, Math.Clamp(num9 * 0.72 + num7 * 0.68, 0.0, 1.2), 1.0 - Math.Pow((num9 + num7 > _sparkBurst) ? 0.0008 : 0.06, num));
			double num18 = num9 * 0.08 + _impact * 0.04 + _burst * 0.04;
			_cameraX = Lerp(_cameraX, Noise(_time * 0.033) * (0.04 + _mid * 0.06 + num18), 1.0 - Math.Pow(0.012, num));
			_cameraY = Lerp(_cameraY, Noise(_time * 0.041 + 12.7) * (0.036 + _mid * 0.05 + num18), 1.0 - Math.Pow(0.012, num));
			_cameraZoom = Lerp(_cameraZoom, 1.0 + _impact * 0.07 + _burst * 0.09 + _bass * 0.035 + _dropEnergy * 0.04 + Noise01(_time * 0.021) * 0.02, 1.0 - Math.Pow(0.018, num));
			UpdateParticles(num);
			UpdateShockwaves(num);
			InvalidateVisual();
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 0.0) && !(actualHeight <= 0.0))
		{
			Point center = default(Point);
			center = new Point(actualWidth * (0.5 + _cameraX), actualHeight * (0.52 + _cameraY));
			SceneWeights sceneWeights = GetSceneWeights();
			DrawAtmosphere(dc, actualWidth, actualHeight, center);
			DrawPlasmaClouds(dc, actualWidth, actualHeight, center, sceneWeights.Plasma);
			DrawShockwaves(dc, actualWidth, actualHeight, center);
			DrawWaveField(dc, actualWidth, actualHeight, center, sceneWeights.Wave);
			DrawTunnel(dc, actualWidth, actualHeight, center, sceneWeights.Tunnel);
			DrawRibbons(dc, actualWidth, actualHeight, center, sceneWeights.Ribbon);
			DrawKaleidoscope(dc, actualWidth, actualHeight, center, sceneWeights.Bloom);
			DrawParticles(dc, actualWidth, actualHeight, center);
			DrawVignette(dc, actualWidth, actualHeight);
		}
	}

	private void TriggerKick(double strength)
	{
		_lastKickTime = _time;
		_impact = Math.Max(_impact, strength * 0.9);
		_burst = Math.Max(_burst, strength * 0.82);
		_sparkBurst = Math.Max(_sparkBurst, strength * 0.52);
		_shockwaves.Add(new Shockwave
		{
			Age = 0.0,
			Strength = strength * 0.78,
			HueOffset = _random.NextDouble() * 4.0,
			Warp = 0.7 + _random.NextDouble() * 0.9
		});
		if (_shockwaves.Count > 8)
		{
			_shockwaves.RemoveAt(0);
		}
		double num = 0.34 + strength * 0.85;
		for (int i = 0; i < _particles.Length; i += 4)
		{
			Particle particle = _particles[i];
			double num2 = particle.X - 0.5;
			double num3 = particle.Y - 0.5;
			double num4 = Math.Max(0.001, Math.Sqrt(num2 * num2 + num3 * num3));
			particle.Vx += num2 / num4 * num * (0.08 + _random.NextDouble() * 0.08);
			particle.Vy += num3 / num4 * num * (0.08 + _random.NextDouble() * 0.08);
			particle.Alpha = Math.Min(1.08, particle.Alpha + strength * 0.16);
		}
	}

	private void UpdateParticles(double delta)
	{
		for (int i = 0; i < _particles.Length; i++)
		{
			Particle particle = _particles[i];
			double num = 0.16 + _bass * 0.12 - _impact * 0.02;
			double num2 = 0.78 + _mid * 2.1 + _dropEnergy * 1.2 + particle.Speed * 0.28;
			double num3 = Noise((particle.X + _time * 0.045) * 2.7 + (double)i * 0.031) * Math.PI;
			double num4 = particle.X - 0.5;
			double num5 = particle.Y - 0.5;
			double num6 = _impact * 0.36 + _burst * 0.24;
			particle.Vx += ((0.0 - num5) * num2 + Math.Cos(num3) * (0.18 + _treble * 0.2) - num4 * num + num4 * num6) * delta * 0.1;
			particle.Vy += (num4 * num2 + Math.Sin(num3) * (0.18 + _treble * 0.2) - num5 * num + num5 * num6) * delta * 0.1;
			particle.Vx *= Math.Pow(0.13, delta);
			particle.Vy *= Math.Pow(0.13, delta);
			particle.X += particle.Vx * delta;
			particle.Y += particle.Vy * delta;
			particle.Depth = Frac(particle.Depth + delta * (0.05 + _bass * 0.08 + _dropEnergy * 0.04) * particle.Speed);
			particle.Alpha = Math.Max(0.18, particle.Alpha - delta * 0.08);
			if (particle.X < -0.14 || particle.X > 1.14 || particle.Y < -0.18 || particle.Y > 1.18)
			{
				ResetParticle(particle, i);
			}
		}
	}

	private void UpdateShockwaves(double delta)
	{
		for (int num = _shockwaves.Count - 1; num >= 0; num--)
		{
			Shockwave value = _shockwaves[num];
			value.Age += delta;
			if (value.Age > 1.45)
			{
				_shockwaves.RemoveAt(num);
			}
			else
			{
				_shockwaves[num] = value;
			}
		}
	}

	private void DrawAtmosphere(DrawingContext dc, double width, double height, Point center)
	{
		Color color = Palette(0.0, 0.72);
		Color color2 = Palette(1.7, 0.62);
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush
		{
			Center = new Point(center.X / width, center.Y / height),
			GradientOrigin = new Point(center.X / width, center.Y / height),
			RadiusX = 0.86 * _cameraZoom,
			RadiusY = 1.05 * _cameraZoom
		};
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(byte.MaxValue, 8, 4, 19), 0.0));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(28.0 + _energy * 34.0 + _impact * 28.0 + _dropEnergy * 18.0, 0.0, 130.0), color.R, color.G, color.B), 0.28));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(14.0 + _mid * 24.0 + _bass * 16.0 + _dropEnergy * 14.0, 0.0, 85.0), color2.R, color2.G, color2.B), 0.62));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(1, 2, 6), 1.0));
		dc.DrawRectangle(radialGradientBrush, null, new Rect(0.0, 0.0, width, height));
		for (int i = 0; i < 3; i++)
		{
			Color color3 = Palette((double)i * 0.9, 0.68);
			double num = width * (0.2 + (double)i * 0.3 + Noise(_time * (0.018 + (double)i * 0.004) + (double)i) * 0.05);
			double num2 = height * (0.34 + Noise(_time * (0.015 + (double)i * 0.005) + 9.0 + (double)i) * 0.34);
			RadialGradientBrush radialGradientBrush2 = new RadialGradientBrush();
			radialGradientBrush2.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(10.0 + _energy * 20.0 + _impact * 32.0, 0.0, 82.0), color3.R, color3.G, color3.B), 0.0));
			radialGradientBrush2.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(4.0 + _energy * 10.0 + _impact * 18.0, 0.0, 45.0), 190, 235, byte.MaxValue), 0.12));
			radialGradientBrush2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
			dc.DrawEllipse(radialGradientBrush2, null, new Point(num, num2), width * (0.16 + _bass * 0.08 + _impact * 0.03), height * (0.22 + _mid * 0.06 + _impact * 0.04));
		}
	}

	private void DrawShockwaves(DrawingContext dc, double width, double height, Point center)
	{
		foreach (Shockwave shockwave in _shockwaves)
		{
			double num = Math.Clamp(shockwave.Age / 1.45, 0.0, 1.0);
			double num2 = 1.0 - Math.Pow(1.0 - num, 2.5);
			double num3 = Math.Pow(1.0 - num, 1.45);
			Color color = Palette(shockwave.HueOffset + num * 1.5, 0.95);
			double num4 = Math.Min(width, height) * (0.08 + num2 * (0.82 + shockwave.Strength * 0.28));
			byte b = (byte)Math.Clamp((62.0 + shockwave.Strength * 78.0) * num3, 0.0, 150.0);
			Pen pen = FrozenPen(Color.FromArgb(b, color.R, color.G, color.B), 1.2 + shockwave.Strength * 4.8 * num3);
			dc.DrawEllipse(null, pen, center, num4 * (2.2 + shockwave.Warp * 0.3), num4 * (0.72 + shockwave.Warp * 0.22));
			RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
			radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp((double)(int)b * 0.42, 0.0, 85.0), color.R, color.G, color.B), 0.0));
			radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp((double)(int)b * 0.12, 0.0, 30.0), 220, 245, byte.MaxValue), 0.16));
			radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
			dc.DrawEllipse(radialGradientBrush, null, center, num4 * 2.5, num4 * 0.9);
		}
	}

	private void DrawPlasmaClouds(DrawingContext dc, double width, double height, Point center, double weight)
	{
		if (!(weight <= 0.02))
		{
			for (int i = 0; i < 4; i++)
			{
				double num = (double)i * Math.PI * 2.0 / 4.0 + _time * (0.026 + (double)i * 0.002) + Noise(_time * 0.023 + (double)i) * 0.45;
				double num2 = (0.16 + Noise01(_time * 0.027 + (double)(i * 3)) * 0.24 + _burst * 0.08 + _impact * 0.05 + _dropEnergy * 0.03) * Math.Min(width, height);
				double num3 = center.X + Math.Cos(num) * num2 * 1.45;
				double num4 = center.Y + Math.Sin(num * 1.3) * num2 * 0.7;
				Color color = Palette((double)i * 0.6 + _time * 0.035, 0.74);
				RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
				radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(weight * (10.0 + _energy * 30.0 + _impact * 40.0), 0.0, 95.0), color.R, color.G, color.B), 0.0));
				radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(weight * (5.0 + _impact * 24.0), 0.0, 38.0), 225, 245, byte.MaxValue), 0.1));
				radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
				dc.DrawEllipse(radialGradientBrush, null, new Point(num3, num4), width * (0.13 + _mid * 0.04 + _impact * 0.04), height * (0.18 + _bass * 0.1 + _impact * 0.05));
			}
		}
	}

	private void DrawWaveField(DrawingContext dc, double width, double height, Point center, double weight)
	{
		if (weight <= 0.02)
		{
			return;
		}
		int num = 4;
		Point val = default(Point);
		Point point = default(Point);
		Point point2 = default(Point);
		Point val2 = default(Point);
		for (int i = 0; i < num; i++)
		{
			double num2 = height * (0.24 + (double)i * 0.14);
			Color color = Palette((double)i * 0.45 + 1.2, 0.76);
			Pen pen = FrozenPen(Color.FromArgb((byte)Math.Clamp(weight * (14.0 + _energy * 42.0 + _impact * 28.0), 0.0, 95.0), color.R, color.G, color.B), 0.9 + _mid * 2.6 + _impact * 1.2);
			StreamGeometry streamGeometry = new StreamGeometry();
			using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
			{
				streamGeometryContext.BeginFigure(new Point(-20.0, num2), isFilled: false, isClosed: false);
				val = new Point(-20.0, num2);
				for (double num3 = 0.0; num3 <= width + 30.0; num3 += width / 8.0)
				{
					double num4 = Noise(_time * 0.18 + num3 * 0.009 + (double)i * 0.7 + _mutation * 3.0);
					double num5 = num2 + num4 * height * (0.055 + _mid * 0.1 + _dropEnergy * 0.045) + Math.Sin(num3 * 0.018 + _time * (1.0 + _bass * 1.1) + (double)i) * height * (0.018 + _impact * 0.025);
					point = new Point(val.X + width / 18.0, val.Y);
					point2 = new Point(num3 - width / 18.0, num5);
					val2 = new Point(num3, num5);
					streamGeometryContext.BezierTo(point, point2, val2, isStroked: true, isSmoothJoin: false);
					val = val2;
				}
			}
			((Freezable)streamGeometry).Freeze();
			dc.DrawGeometry(null, pen, streamGeometry);
		}
	}

	private void DrawTunnel(DrawingContext dc, double width, double height, Point center, double weight)
	{
		if (!(weight <= 0.02))
		{
			int num = 10;
			for (int num2 = num; num2 >= 0; num2--)
			{
				double num3 = Frac((double)num2 / (double)num + _time * (0.045 + _bass * 0.055 + _dropEnergy * 0.02));
				double num4 = Math.Pow(num3, 1.8) * Math.Min(width, height) * (0.5 + _burst * 0.14 + _impact * 0.12);
				Color color = Palette(num3 * 3.0 + _time * 0.04, 0.82);
				Pen pen = FrozenPen(Color.FromArgb((byte)Math.Clamp(weight * num3 * (18.0 + _energy * 52.0 + _impact * 58.0), 0.0, 130.0), color.R, color.G, color.B), 0.8 + num3 * 2.8 + _burst * 2.2 + _impact * 2.6);
				dc.DrawEllipse(null, pen, center, num4 * 2.15, num4 * 0.72);
			}
		}
	}

	private void DrawRibbons(DrawingContext dc, double width, double height, Point center, double weight)
	{
		if (weight <= 0.02)
		{
			return;
		}
		for (int i = 0; i < 3; i++)
		{
			Color color = Palette((double)i * 0.68 + _time * 0.025, 0.9);
			for (int num = 4; num >= 0; num--)
			{
				byte a = (byte)Math.Clamp(weight * (18.0 + _energy * 54.0 + _impact * 42.0) * (1.0 - (double)num * 0.13), 0.0, 125.0);
				Pen pen = FrozenPen(thickness: Math.Max(0.7, (double)(5 - num) * (0.36 + _energy * 0.36 + _impact * 0.18)), color: Color.FromArgb(a, color.R, color.G, color.B));
				double num2 = _time - (double)num * 0.13;
				dc.DrawGeometry(null, pen, BuildRibbon(width, height, center, i, num2, 1));
				dc.DrawGeometry(null, pen, BuildRibbon(width, height, center, i + 3, (0.0 - num2) * 0.92, -1));
			}
		}
	}

	private Geometry BuildRibbon(double width, double height, Point center, int layer, double t, int mirror)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using StreamGeometryContext streamGeometryContext = streamGeometry.Open();
		double num = center.Y + (double)mirror * height * (Noise(t * 0.041 + (double)layer) * 0.16 + (double)(layer - 2) * 0.035);
		double num2 = height * (0.09 + _mid * 0.13 + _dropEnergy * 0.055 + _impact * 0.045 + Noise01(t * 0.033 + (double)layer * 1.7) * 0.055);
		Point val = new Point((0.0 - width) * 0.12, num + Math.Sin(t + (double)layer) * num2 * 0.35);
		streamGeometryContext.BeginFigure(val, isFilled: false, isClosed: false);
		Point val2 = val;
		Point val3 = default(Point);
		Point point = default(Point);
		Point point2 = default(Point);
		for (int i = 0; i < 5; i++)
		{
			double num3 = width * (0.12 + (double)i * 0.25);
			double num4 = t * (0.76 + (double)layer * 0.06 + _bass * 0.36) + (double)i * 0.82 + Noise(t * 0.025 + (double)i + (double)layer) * (1.05 + _mid * 0.6);
			val3 = new Point(num3, num + Math.Sin(num4) * num2 + Math.Cos(num4 * 0.43 + (double)layer) * num2 * 0.42);
			point = new Point(val2.X + width * 0.11, val2.Y + Math.Cos(num4) * num2 * 0.35);
			point2 = new Point(val3.X - width * 0.11, val3.Y - Math.Sin(num4 * 1.2) * num2 * 0.35);
			streamGeometryContext.BezierTo(point, point2, val3, isStroked: true, isSmoothJoin: false);
			val2 = val3;
		}
		((Freezable)streamGeometry).Freeze();
		return streamGeometry;
	}

	private void DrawKaleidoscope(DrawingContext dc, double width, double height, Point center, double weight)
	{
		if (weight <= 0.02)
		{
			return;
		}
		int num = 4;
		double num2 = Math.Min(width, height) * (0.36 + _burst * 0.18 + _impact * 0.12);
		for (int i = 0; i < num; i++)
		{
			double angle = (double)i * 360.0 / (double)num + _time * (6.0 + _mid * 12.0 + _dropEnergy * 5.0) + Noise(_time * 0.05 + (double)i) * (10.0 + _impact * 7.0);
			dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
			Color color = Palette((double)i * 0.5 + _time * 0.055, 0.95);
			Pen pen = FrozenPen(Color.FromArgb((byte)Math.Clamp(weight * (14.0 + _treble * 54.0 + _sparkBurst * 34.0), 0.0, 98.0), color.R, color.G, color.B), 0.9 + _treble * 2.3 + _sparkBurst * 1.2);
			StreamGeometry streamGeometry = new StreamGeometry();
			using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
			{
				streamGeometryContext.BeginFigure(center, isFilled: false, isClosed: false);
				streamGeometryContext.BezierTo(new Point(center.X + num2 * 0.18, center.Y - num2 * 0.22), new Point(center.X + num2 * 0.52, center.Y + num2 * (0.18 + Noise(_time * 0.09 + (double)i) * 0.18)), new Point(center.X + num2, center.Y + Noise(_time * 0.11 + (double)(i * 2)) * num2 * 0.12), isStroked: true, isSmoothJoin: false);
			}
			((Freezable)streamGeometry).Freeze();
			dc.DrawGeometry(null, pen, streamGeometry);
			dc.Pop();
		}
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(weight * (28.0 + _burst * 80.0 + _impact * 72.0), 0.0, 150.0), 230, 250, byte.MaxValue), 0.0));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(weight * (24.0 + _impact * 42.0), 0.0, 82.0), 74, 210, 238), 0.28));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
		dc.DrawEllipse(radialGradientBrush, null, center, width * (0.08 + _burst * 0.06 + _impact * 0.04), height * (0.18 + _burst * 0.08 + _impact * 0.06));
	}

	private void DrawParticles(DrawingContext dc, double width, double height, Point center)
	{
		for (int i = 0; i < _particles.Length; i++)
		{
			if (i % 3 != 0 || !(_energy < 0.45) || !(_sparkBurst < 0.25))
			{
				Particle particle = _particles[i];
				double num = 0.72 + particle.Depth * 0.68;
				double num2 = center.X + (particle.X - 0.5) * width * num * _cameraZoom;
				double num3 = center.Y + (particle.Y - 0.5) * height * num * _cameraZoom;
				Color color = Palette(particle.ColorOffset + particle.Depth * 2.0, 0.85);
				byte b = (byte)Math.Clamp((12.0 + _treble * 58.0 + _sparkBurst * 48.0 + _dropEnergy * 20.0) * (0.36 + particle.Depth) * particle.Alpha, 0.0, 150.0);
				double num4 = particle.Size * (0.55 + particle.Depth * 1.55 + _burst * 1.1 + _impact * 1.25);
				SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromArgb(b, color.R, color.G, color.B));
				((Freezable)solidColorBrush).Freeze();
				dc.DrawEllipse(solidColorBrush, null, new Point(num2, num3), num4, num4);
				if ((_treble > 0.32 || _sparkBurst > 0.22) && particle.Depth > 0.46)
				{
					Pen pen = FrozenPen(Color.FromArgb((byte)Math.Clamp((double)(int)b * 0.42, 0.0, 82.0), color.R, color.G, color.B), 0.5 + _sparkBurst * 0.75);
					dc.DrawLine(pen, new Point(num2 - particle.Vx * width * (0.35 + _impact * 0.45), num3 - particle.Vy * height * (0.35 + _impact * 0.45)), new Point(num2, num3));
				}
			}
		}
	}

	private void DrawVignette(DrawingContext dc, double width, double height)
	{
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush
		{
			Center = new Point(0.5, 0.5),
			GradientOrigin = new Point(0.5, 0.5),
			RadiusX = 0.7,
			RadiusY = 0.9
		};
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.3));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(226, 0, 0, 0), 1.0));
		dc.DrawRectangle(radialGradientBrush, null, new Rect(0.0, 0.0, width, height));
	}

	private SceneWeights GetSceneWeights()
	{
		double num = Weight(_time, 0.017, 0.2);
		double num2 = Weight(_time, 0.013, 1.9);
		double num3 = Weight(_time, 0.011, 3.4);
		double num4 = Weight(_time, 0.019, 5.2);
		double num5 = Weight(_time, 0.015, 7.8);
		double num6 = num + (_mid * 0.34 + _dropEnergy * 0.12);
		num2 += _bass * 0.52 + _impact * 0.34;
		num4 += _burst * 0.34 + _impact * 0.38;
		num5 += _treble * 0.2 + _mid * 0.14;
		num3 += _mutation * 0.12 + _dropEnergy * 0.22;
		double num7 = num6 + num2 + num3 + num4 + num5;
		return new SceneWeights(num6 / num7, num2 / num7, num3 / num7, num4 / num7, num5 / num7);
	}

	private static double Weight(double time, double speed, double phase)
	{
		double value = 0.5 + 0.5 * Math.Sin(time * speed + phase + Math.Sin(time * speed * 0.37 + phase) * 1.6);
		return 0.24 + SmoothStep(value) * 0.82;
	}

	private Color Palette(double offset, double saturation)
	{
		double num = _time * 0.028 + _mutation * 2.4 + offset;
		Color a = _palette[(int)Math.Floor(num) % _palette.Length];
		Color b = _palette[((int)Math.Floor(num) + 1) % _palette.Length];
		double t = SmoothStep(Frac(num));
		Color color = Mix(a, b, t);
		double num2 = 18.0 * (1.0 - saturation);
		return Color.FromRgb((byte)Math.Clamp((double)(int)color.R * saturation + num2, 0.0, 255.0), (byte)Math.Clamp((double)(int)color.G * saturation + num2, 0.0, 255.0), (byte)Math.Clamp((double)(int)color.B * saturation + num2, 0.0, 255.0));
	}

	private Particle CreateParticle(int index)
	{
		double num = _random.NextDouble() * Math.PI * 2.0;
		double num2 = Math.Sqrt(_random.NextDouble()) * 0.47;
		return new Particle
		{
			X = 0.5 + Math.Cos(num) * num2,
			Y = 0.5 + Math.Sin(num) * num2,
			Vx = Noise((double)index * 1.17) * 0.02,
			Vy = Noise((double)index * 2.11 + 4.0) * 0.02,
			Depth = _random.NextDouble(),
			Speed = 0.6 + _random.NextDouble() * 1.7,
			Size = 0.45 + _random.NextDouble() * 1.15,
			Alpha = 0.28 + _random.NextDouble() * 0.62,
			ColorOffset = _random.NextDouble() * 4.0
		};
	}

	private void ResetParticle(Particle particle, int index)
	{
		Particle particle2 = CreateParticle(index);
		particle.X = particle2.X;
		particle.Y = particle2.Y;
		particle.Vx = particle2.Vx;
		particle.Vy = particle2.Vy;
		particle.Depth = particle2.Depth;
		particle.Speed = particle2.Speed;
		particle.Size = particle2.Size;
		particle.Alpha = particle2.Alpha;
		particle.ColorOffset = particle2.ColorOffset;
	}

	private static Pen FrozenPen(Color color, double thickness)
	{
		Pen obj = new Pen(new SolidColorBrush(color), thickness)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
			LineJoin = PenLineJoin.Round
		};
		((Freezable)obj).Freeze();
		return obj;
	}

	private static Color Mix(Color a, Color b, double t)
	{
		return Color.FromRgb((byte)Math.Clamp(Lerp((int)a.R, (int)b.R, t), 0.0, 255.0), (byte)Math.Clamp(Lerp((int)a.G, (int)b.G, t), 0.0, 255.0), (byte)Math.Clamp(Lerp((int)a.B, (int)b.B, t), 0.0, 255.0));
	}

	private static double Noise01(double value)
	{
		return Noise(value) * 0.5 + 0.5;
	}

	private static double Noise(double value)
	{
		double num = Math.Floor(value);
		double num2 = value - num;
		double t = num2 * num2 * (3.0 - 2.0 * num2);
		return Lerp(Hash(num), Hash(num + 1.0), t);
	}

	private static double Hash(double value)
	{
		return Frac(Math.Sin(value * 127.1 + 311.7) * 43758.5453123) * 2.0 - 1.0;
	}

	private static double SmoothPulse(double value)
	{
		double num = Math.Max(0.0, Math.Sin(value));
		return num * num * num * num;
	}

	private static double SmoothStep(double value)
	{
		value = Math.Clamp(value, 0.0, 1.0);
		return value * value * (3.0 - 2.0 * value);
	}

	private static double Lerp(double a, double b, double t)
	{
		return a + (b - a) * Math.Clamp(t, 0.0, 1.0);
	}

	private static double Frac(double value)
	{
		return value - Math.Floor(value);
	}
}
