using System;
using System.Collections.Generic;
using GalgameManager.Models;

namespace PotatoVN.App.PluginBase.Models;

// Enums
public enum GamepadButton { A, B, X, Y, Up, Down, Left, Right, Start, Select, Guide }
public enum SortType { LastPlayTime, AddTime }

// Input
public record GamepadInputMessage(GamepadButton Button);
public record SortChangedMessage(SortType Type, bool Ascending);

// Hints
public record HintAction(string Label, string Button, Action? Action = null);
public record UpdateHintsMessage(List<HintAction> Hints);

// Navigation
public record NavigateToDetailMessage(Galgame Game);
public record NavigateToLibraryMessage();
public record GoBackMessage();

// Actions
public record UnhandledGamepadInputMessage(GamepadButton Button);
public record LaunchGameMessage(Galgame Game);
public record AppExitMessage();