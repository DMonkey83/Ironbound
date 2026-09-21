using System;
using System.Linq;
using Godot;

/// <summary>
/// The part of <see cref="Main"/> that decides what the player is looking at.
/// </summary>
/// <remarks>
/// The camera used to be bolted to one spot at one zoom, fitted to the window's height and
/// nothing else — so a figure was forty pixels tall whatever had been modelled onto it, and a
/// narrow window simply cut the far side of the fight off.
/// <para>
/// The rig's state lives here rather than on the node, because <c>RebuildWorld</c> frees the
/// camera along with everything else and the view should survive a new chapter. Picking needs no
/// help: it casts a ray from whatever the camera currently is onto the ground.
/// </para>
/// </remarks>
public partial class Main
{
	/// <summary>How far above the horizon the camera sits. Fixed: it is the look of the game.</summary>
	private static readonly float CameraPitch = Mathf.Atan2(13f, 12f * Mathf.Sqrt2);

	private const float CameraDistance = 30f;
	private const float ClosestZoom = 4f;

	private Vector3 _camFocus;
	private float _camYaw = Mathf.Pi / 4f;
	private float _camZoom = 16f;
	private float _fitZoom = 16f;
	private Vector2I _camBoard;

	/// <summary>True once the player has taken the camera. Stops the auto-fit fighting them.</summary>
	private bool _camManual;

	private Vector3? _grabbed;
	// Two, because they are about different things and must not cancel each other: an enemy
	// acting halfway through a turn of the camera would otherwise leave it at an odd angle.
	private Tween _yawTween;
	private Tween _focusTween;

	// ---- mounting and applying ----

	/// <summary>Called by RebuildWorld once the camera node exists.</summary>
	private void MountCamera(int width, int height)
	{
		var board = new Vector2I(width, height);

		if (board != _camBoard)
		{
			// A different battlefield: whatever the player had framed is about somewhere else.
			_camBoard = board;
			_camManual = false;
		}

		if (_camManual)
		{
			ApplyCamera();
		}
		else
		{
			FitBoard();
		}
	}

	/// <summary>From the focus towards the camera, for a given turn about the board.</summary>
	private static Vector3 CameraOffset(float yaw) => new(
		Mathf.Sin(yaw) * Mathf.Cos(CameraPitch),
		Mathf.Sin(CameraPitch),
		Mathf.Cos(yaw) * Mathf.Cos(CameraPitch));

	/// <summary>The one place that writes to the camera node.</summary>
	private void ApplyCamera()
	{
		if (_camera is null || !IsInstanceValid(_camera))
		{
			return;
		}

		_camera.Position = _camFocus + (CameraOffset(_camYaw) * CameraDistance);
		_camera.LookAt(_camFocus);
		_camera.Size = _camZoom;

		var scale = Vector3.One * LabelScale();
		foreach (var nameplate in _nameplates.Values)
		{
			if (IsInstanceValid(nameplate))
			{
				nameplate.Scale = scale;
			}
		}
	}

	/// <summary>
	/// How big to draw text in the world so that it holds its size on the screen.
	/// </summary>
	/// <remarks>
	/// Nameplates are objects in the scene, so zooming in on a goblin used to zoom in on his
	/// name as well, until it was bigger than he was. Shrunk in step with the zoom they stay
	/// the size they are at the fitted view — down to a floor, because past a point a label
	/// that refuses to grow at all starts to look lost beside the figure it names.
	/// </remarks>
	private float LabelScale() =>
		_fitZoom <= 0 ? 1f : Mathf.Clamp(_camZoom / _fitZoom, 0.30f, 1f);

	// ---- fitting the board to whatever the window is ----

	/// <summary>
	/// The part of the screen the HUD leaves alone, as fractions of the whole: the roster runs
	/// along the top, the controls across the bottom, and the log down the right when it is open.
	/// </summary>
	private Rect2 FreeRect()
	{
		const float top = 0.075f;
		const float bottom = 0.74f;
		var right = _logPanel is { Visible: true } ? 0.74f : 1f;

		return new Rect2(0f, top, right, bottom - top);
	}

	/// <summary>
	/// Frames the whole board in the free part of the screen, whatever shape the window is.
	/// </summary>
	/// <remarks>
	/// An orthographic camera's size is its <em>vertical</em> extent. Fitting to that alone is
	/// what clipped the goblins off the side of a half-width window; the width has to be fitted
	/// too, divided by the aspect ratio, and the larger of the two wins.
	/// </remarks>
	private void FitBoard()
	{
		var view = GetViewport().GetVisibleRect().Size;
		if (view.X <= 0 || view.Y <= 0 || _camBoard == Vector2I.Zero)
		{
			return;
		}

		var z = CameraOffset(_camYaw);
		var x = Vector3.Up.Cross(z).Normalized();
		var y = z.Cross(x);

		var centre = new Vector3(_camBoard.X / 2f, 0, _camBoard.Y / 2f);

		// The four corners on the ground, and again at about head height so nobody standing on
		// the back row has their nameplate cut off.
		var corners =
			from cx in new[] { 0f, _camBoard.X }
			from cz in new[] { 0f, _camBoard.Y }
			from cy in new[] { 0f, 2.2f }
			select new Vector3(cx, cy, cz) - centre;

		var across = corners.Select(c => c.Dot(x)).ToList();
		var upward = corners.Select(c => c.Dot(y)).ToList();
		var wide = across.Max() - across.Min();
		var tall = upward.Max() - upward.Min();
		var midAcross = (across.Max() + across.Min()) / 2f;
		var midUpward = (upward.Max() + upward.Min()) / 2f;

		var free = FreeRect();
		var aspect = view.X / view.Y;

		_fitZoom = Mathf.Max(tall / free.Size.Y, wide / (aspect * free.Size.X)) * 1.06f;
		_camZoom = _fitZoom;

		// The middle of the free rectangle is not the middle of the screen. Shift the focus so
		// the board sits in what can be seen rather than half behind the controls.
		var offX = free.GetCenter().X - 0.5f;
		var offY = free.GetCenter().Y - 0.5f;

		_camFocus = centre
			+ (x * (midAcross - (offX * _camZoom * aspect)))
			+ (y * (midUpward + (offY * _camZoom)));

		ApplyCamera();
	}

	private void OnViewportResized()
	{
		if (!_camManual)
		{
			FitBoard();
		}
	}

	// ---- the ground under a point on the screen ----

	/// <summary>
	/// Where a ray through a screen point meets the ground. Picking, zooming toward the cursor
	/// and dragging the board all ask exactly this, so it is asked in one place.
	/// </summary>
	private Vector3? GroundUnder(Vector2 screen)
	{
		if (_camera is null || !IsInstanceValid(_camera))
		{
			return null;
		}

		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);

		if (Mathf.IsZeroApprox(direction.Y))
		{
			return null;
		}

		// Straight onto the ground plane. An orthographic camera over flat ground needs no physics.
		var distance = -from.Y / direction.Y;
		return distance < 0 ? null : from + (direction * distance);
	}

	// ---- what the player can do to it ----

	/// <summary>Zooms so that whatever was under the given screen point still is.</summary>
	private void ZoomAt(Vector2 screen, float factor)
	{
		var before = GroundUnder(screen);

		_camZoom = Mathf.Clamp(_camZoom * factor, ClosestZoom, _fitZoom * 1.5f);
		ApplyCamera();

		if (before is { } was && GroundUnder(screen) is { } now)
		{
			_camFocus += was - now;
		}

		_camManual = true;
		KeepBoardInReach();
		ApplyCamera();
	}

	private void PanBy(Vector3 ground)
	{
		_focusTween?.Kill();
		_camFocus += ground;
		_camManual = true;
		KeepBoardInReach();
		ApplyCamera();
	}

	/// <summary>Turns about the focus in steps of an eighth, which keeps the grid readable.</summary>
	private void RotateBy(int eighths)
	{
		// From the nearest eighth, so tapping Q twice quickly still lands on a clean angle.
		var settled = Mathf.Round(_camYaw / (Mathf.Pi / 4f)) * (Mathf.Pi / 4f);
		var target = settled + (eighths * Mathf.Pi / 4f);

		_yawTween?.Kill();
		_yawTween = CreateTween();
		_yawTween.TweenMethod(Callable.From<float>(yaw =>
		{
			_camYaw = yaw;
			ApplyCamera();
		}), _camYaw, target, 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
	}

	private void ResetCamera()
	{
		_yawTween?.Kill();
		_focusTween?.Kill();
		_camManual = false;
		FitBoard();
	}

	/// <summary>The focus may wander, but never so far that the board cannot be found again.</summary>
	private void KeepBoardInReach()
	{
		const float margin = 3f;
		_camFocus = new Vector3(
			Mathf.Clamp(_camFocus.X, -margin, _camBoard.X + margin),
			_camFocus.Y,
			Mathf.Clamp(_camFocus.Z, -margin, _camBoard.Y + margin));
	}

	/// <summary>Wheel, middle-drag and the rotate/reset keys. True if the event was the camera's.</summary>
	private bool CameraInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
				ZoomAt(wheel.Position, 0.9f);
				return true;

			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
				ZoomAt(wheel.Position, 1.1f);
				return true;

			case InputEventMouseButton { ButtonIndex: MouseButton.Middle } button:
				_grabbed = button.Pressed ? GroundUnder(button.Position) : null;
				return true;

			case InputEventMouseMotion motion when _grabbed is { } held:
				// Whatever was grabbed goes back under the cursor: the board is dragged, not the view.
				if (GroundUnder(motion.Position) is { } now)
				{
					PanBy(held - now);
				}

				return true;

			case InputEventKey { Pressed: true, Echo: false } key:
				switch (key.Keycode)
				{
					case Key.Q:
						RotateBy(1);
						return true;
					case Key.E:
						RotateBy(-1);
						return true;
					case Key.Home or Key.F:
						ResetCamera();
						return true;
				}

				break;
		}

		return false;
	}

	/// <summary>W A S D, polled. Arrow keys are left alone: Godot's buttons use them for focus.</summary>
	private void CameraKeys(double delta)
	{
		var right = (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f);
		var ahead = (Input.IsKeyPressed(Key.W) ? 1f : 0f) - (Input.IsKeyPressed(Key.S) ? 1f : 0f);

		if (right == 0f && ahead == 0f)
		{
			return;
		}

		var z = CameraOffset(_camYaw);
		var across = Vector3.Up.Cross(z).Normalized();
		var forward = new Vector3(-z.X, 0, -z.Z).Normalized();

		PanBy(((across * right) + (forward * ahead)) * _camZoom * 0.8f * (float)delta);
	}

	// ---- keeping the action in view ----

	/// <summary>
	/// Brings a figure on screen if a beat is about to happen somewhere the player cannot see.
	/// </summary>
	/// <remarks>
	/// Only matters once the player has zoomed in, which is exactly when an enemy's whole turn
	/// can happen off the edge. It leaves the zoom alone and does not count as the player taking
	/// the camera, so Home still means what it meant.
	/// </remarks>
	private void Follow(Node3D figure)
	{
		if (_instant || _grabbed is not null || _camera is null || !IsInstanceValid(_camera))
		{
			return;
		}

		var view = GetViewport().GetVisibleRect().Size;
		var free = FreeRect();
		var at = _camera.UnprojectPosition(figure.Position + new Vector3(0, 0.7f, 0)) / view;

		// Comfortably inside what the HUD leaves free: nothing to do.
		var calm = new Rect2(free.Position + (free.Size * 0.15f), free.Size * 0.70f);
		if (calm.HasPoint(at))
		{
			return;
		}

		if (GroundUnder(free.GetCenter() * view) is not { } middle)
		{
			return;
		}

		var target = _camFocus + (figure.Position - middle);

		_focusTween?.Kill();
		_focusTween = CreateTween();
		_focusTween.TweenMethod(Callable.From<Vector3>(focus =>
		{
			_camFocus = focus;
			ApplyCamera();
		}), _camFocus, target, 0.30f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
	}

	// ---- a scripted tour, for checking all of the above without a hand on the mouse ----

	/// <summary>
	/// godot -- --camera-tour: drives the rig through the same methods the input handlers call.
	/// Recorded with --write-movie, it proves everything behind the input; the input itself
	/// still wants a person.
	/// </summary>
	private void RunCameraTour()
	{
		var view = GetViewport().GetVisibleRect().Size;
		var toward = view * new Vector2(0.62f, 0.52f);
		var tour = CreateTween();

		tour.TweenInterval(1.0);
		for (var i = 0; i < 9; i++)
		{
			tour.TweenCallback(Callable.From(() => ZoomAt(toward, 0.9f)));
			tour.TweenInterval(0.08);
		}

		tour.TweenInterval(2.5);
		tour.TweenCallback(Callable.From(() => RotateBy(-1)));
		tour.TweenInterval(1.6);
		tour.TweenCallback(Callable.From(() => RotateBy(-1)));
		tour.TweenInterval(1.6);
		tour.TweenCallback(Callable.From(() => PanBy(new Vector3(-2.5f, 0, 1.5f))));
		tour.TweenInterval(1.6);
		tour.TweenCallback(Callable.From(() => RotateBy(2)));
		tour.TweenInterval(1.5);
		tour.TweenCallback(Callable.From(ResetCamera));
	}
}
