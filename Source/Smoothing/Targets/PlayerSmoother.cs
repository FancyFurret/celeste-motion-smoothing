﻿using Celeste.Mod.MotionSmoothing.Interop;
using Celeste.Mod.MotionSmoothing.Smoothing.States;
using Celeste.Mod.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.MotionSmoothing.Smoothing.Targets;

public static class PlayerSmoother
{
    private static MInput.KeyboardData _keyboardData;
    private static MInput.MouseData _mouseData;
    private static MInput.GamePadData[] _gamePadData;

    private static bool _cancelExtrapolationUntilNextUpdate;
    private static bool _dashPressed;

    public static void Hook()
    {
        _keyboardData = new MInput.KeyboardData();
        _mouseData = new MInput.MouseData();
        _gamePadData = new MInput.GamePadData[4];
        for (var i = 0; i < _gamePadData.Length; i++)
            _gamePadData[i] = new MInput.GamePadData(i);

        On.Monocle.Engine.Update += EngineUpdateHook;
    }

    public static void Unhook()
    {
        On.Monocle.Engine.Update -= EngineUpdateHook;
    }

    private static void EngineUpdateHook(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime)
    {
        _cancelExtrapolationUntilNextUpdate = false;

        if (!Input.Dash.Check && !Input.CrouchDash.Check)
            _dashPressed = false;

        orig(self, gameTime);
    }

    public static Vector2 Smooth(Player player, IPositionSmoothingState state, double elapsed, SmoothingMode mode)
    {
        return mode switch
        {
            SmoothingMode.Interpolate => Interpolate(player, state, elapsed),
            SmoothingMode.Extrapolate => Extrapolate(player, state, elapsed),
            _ => state.OriginalRealPosition
        };
    }

    private static Vector2 Interpolate(Player player, IPositionSmoothingState state, double elapsed)
    {
        if (ActorPushTracker.ApplyPusherOffset(player, elapsed, SmoothingMode.Interpolate, out var pushed))
            return pushed;
        
        return SmoothingMath.Interpolate(state.RealPositionHistory, elapsed);
    }

    private static Vector2 Extrapolate(Player player, IPositionSmoothingState state, double elapsed)
    {
        // Disable during screen transitions or pause
        if (Engine.Scene is Level { Transitioning: true } or { Paused: true } || Engine.FreezeTimer > 0)
            return state.OriginalRealPosition;

        // To keep physics consistent, input is still only updated at 60FPS, but we want to check if there is input
        // during the extrapolation. So temporarily update the input to the current frame.
        UpdateInput();

        var dashInput = Input.Dash.Check || Input.CrouchDash.Check;

        // Reset the input back so that physics is still consistent
        ResetInput();

        // If the player is about to dash, reset the states so the player position stops going in the wrong direction
        if (!_dashPressed && dashInput)
        {
            _dashPressed = true;
            _cancelExtrapolationUntilNextUpdate = true;
        }

        if (_cancelExtrapolationUntilNextUpdate)
            return state.OriginalRealPosition;

        // Check if the player is inverted and flip the speed accordingly
        var speed = player.Speed;
        if (GravityHelperImports.IsPlayerInverted?.Invoke() == true)
            speed.Y *= -1;
        
        if (ActorPushTracker.ApplyPusherOffset(player, elapsed, SmoothingMode.Extrapolate, out var pushed))
            return pushed + speed * Engine.TimeRate * Engine.TimeRateB * (float)elapsed;

        return SmoothingMath.Extrapolate(state.RealPositionHistory, elapsed);
    }

    private static void UpdateInput()
    {
        _keyboardData.PreviousState = MInput.Keyboard.PreviousState;
        _keyboardData.CurrentState = MInput.Keyboard.CurrentState;
        _mouseData.PreviousState = MInput.Mouse.PreviousState;
        _mouseData.CurrentState = MInput.Mouse.CurrentState;
        for (var i = 0; i < _gamePadData.Length; i++)
        {
            _gamePadData[i].PreviousState = MInput.GamePads[i].PreviousState;
            _gamePadData[i].CurrentState = MInput.GamePads[i].CurrentState;
            _gamePadData[i].Attached = MInput.GamePads[i].Attached;
            _gamePadData[i].HadInputThisFrame = MInput.GamePads[i].HadInputThisFrame;
            _gamePadData[i].rumbleStrength = MInput.GamePads[i].rumbleStrength;
            _gamePadData[i].rumbleStrength = MInput.GamePads[i].rumbleStrength;
        }

        MInput.Keyboard.Update();
        MInput.Mouse.Update();
        for (var i = 0; i < _gamePadData.Length; i++)
            MInput.GamePads[i].Update();
    }

    private static void ResetInput()
    {
        MInput.Keyboard.PreviousState = _keyboardData.PreviousState;
        MInput.Keyboard.CurrentState = _keyboardData.CurrentState;
        MInput.Mouse.PreviousState = _mouseData.PreviousState;
        MInput.Mouse.CurrentState = _mouseData.CurrentState;
        for (var i = 0; i < _gamePadData.Length; i++)
        {
            MInput.GamePads[i].PreviousState = _gamePadData[i].PreviousState;
            MInput.GamePads[i].CurrentState = _gamePadData[i].CurrentState;
            MInput.GamePads[i].Attached = _gamePadData[i].Attached;
            MInput.GamePads[i].HadInputThisFrame = _gamePadData[i].HadInputThisFrame;
            MInput.GamePads[i].rumbleStrength = _gamePadData[i].rumbleStrength;
            MInput.GamePads[i].rumbleStrength = _gamePadData[i].rumbleStrength;
        }
    }
}