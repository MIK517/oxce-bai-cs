using Oxce.Engine.Input;
using Oxce.Rendering;

namespace Oxce.Engine;

/// <summary>
/// The reference's state stack (<c>Game::_states</c>): screens are pushed above one another, only the top one
/// thinks and receives input, and a screen that asks to exit is popped. A screen is an
/// <see cref="IIndexedLoopClient"/>, so geoscape, logistics and battle screens share one contract. The stack is
/// client state; a host session reads <see cref="Covers"/> to learn that a screen sits above another (the pause
/// policy of the geoscape) and never lets the stack decide gameplay.
/// </summary>
public sealed class ScreenNavigator
{
    private readonly List<Entry> _screens = [];
    private readonly IIndexedLoopClient? _root;
    private long _retiredRevisions;

    public ScreenNavigator()
    {
    }

    /// <summary>
    /// A stack with a root: the screen the host loop shows when nothing is pushed (the geoscape). The root is not an
    /// entry, so it never counts or pops; it receives input and ticks while the stack is empty, and
    /// <see cref="Covers"/> tells whether anything sits above it.
    /// </summary>
    public ScreenNavigator(IIndexedLoopClient root) => _root = root ?? throw new ArgumentNullException(nameof(root));

    private sealed record Entry(IIndexedLoopClient Screen, bool Opaque, Action? Closed);

    /// <summary>The root screen the stack was created with, if any.</summary>
    public IIndexedLoopClient? Root => _root;

    public int Count => _screens.Count;

    public IIndexedLoopClient? Top => _screens.Count == 0 ? null : _screens[^1].Screen;

    /// <summary>Screens that draw, bottom first (the root is never listed; a host draws it when every entry is translucent): the topmost opaque screen and every translucent one above it.</summary>
    public IReadOnlyList<IIndexedLoopClient> Visible
    {
        get
        {
            var first = _screens.FindLastIndex(static entry => entry.Opaque);
            return [.. _screens.Skip(Math.Max(first, 0)).Select(static entry => entry.Screen)];
        }
    }

    /// <summary>Changes whenever a screen is pushed, popped or replaced or a screen's own revision changes.</summary>
    public long PresentationRevision => checked(_retiredRevisions + _screens.Sum(static entry => entry.Screen.PresentationRevision));

    /// <summary>True when <paramref name="screen"/> is on the stack and something sits above it; for the root, when anything is on the stack.</summary>
    public bool Covers(IIndexedLoopClient screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (ReferenceEquals(screen, _root)) return _screens.Count != 0;
        var index = _screens.FindIndex(entry => ReferenceEquals(entry.Screen, screen));
        return index >= 0 && index < _screens.Count - 1;
    }

    /// <summary>
    /// Removes a screen wherever it sits (a popup whose reason ended below a later screen). The screen revealed by
    /// removing the top is told, as for <see cref="Pop"/>. Returns false when the screen is not on the stack.
    /// </summary>
    public bool Remove(IIndexedLoopClient screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var index = _screens.FindIndex(entry => ReferenceEquals(entry.Screen, screen));
        if (index < 0) return false;
        var wasTop = index == _screens.Count - 1;
        var entry = _screens[index];
        _screens.RemoveAt(index);
        _retiredRevisions = checked(_retiredRevisions + entry.Screen.PresentationRevision + 1);
        entry.Closed?.Invoke();
        if (wasTop && _screens.Count != 0 && _screens[^1].Screen is IRevealedScreen revealed) revealed.Revealed();
        return true;
    }

    /// <param name="screen">The screen to show.</param>
    /// <param name="opaque">False lets the screen below show through (a popup); the host draws <see cref="Visible"/> in order.</param>
    /// <param name="closed">Runs after the screen leaves the stack, by exit request, <see cref="Pop"/>, <see cref="Replace"/> or <see cref="Remove"/>.</param>
    public void Push(IIndexedLoopClient screen, bool opaque = true, Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (_screens.Exists(entry => ReferenceEquals(entry.Screen, screen)))
            throw new InvalidOperationException("The screen is already on the stack.");
        _screens.Add(new(screen, opaque, closed));
        _retiredRevisions = checked(_retiredRevisions + 1);
    }

    /// <summary>
    /// Removes the top screen and tells the screen it reveals (<see cref="IRevealedScreen"/>, the reference's <c>State::init</c> after a pop).
    /// Returns false when the stack is empty.
    /// </summary>
    public bool Pop()
    {
        if (!Remove()) return false;
        if (_screens.Count != 0 && _screens[^1].Screen is IRevealedScreen revealed) revealed.Revealed();
        return true;
    }

    private bool Remove()
    {
        if (_screens.Count == 0) return false;
        var entry = _screens[^1];
        _screens.RemoveAt(_screens.Count - 1);
        _retiredRevisions = checked(_retiredRevisions + entry.Screen.PresentationRevision + 1);
        entry.Closed?.Invoke();
        return true;
    }

    /// <summary>Pops the top screen, if any, and pushes <paramref name="screen"/> in its place.</summary>
    public void Replace(IIndexedLoopClient screen, bool opaque = true, Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        // The screen below is not revealed: the new screen takes the place at once.
        Remove();
        Push(screen, opaque, closed);
    }

    /// <summary>Routes input to the top screen only (the root when the stack is empty). Returns false when there is nothing to route to.</summary>
    public bool HandleInput(in GameInputEvent input)
    {
        if (_screens.Count == 0)
        {
            if (_root is null) return false;
            _root.HandleInput(input);
            return true;
        }
        var top = _screens[^1].Screen;
        top.HandleInput(input);
        PopExited(top);
        return true;
    }

    /// <summary>Advances the top screen only, as the reference thinks only its top state.</summary>
    public bool Tick(TimeSpan fixedInterval)
    {
        if (_screens.Count == 0)
        {
            if (_root is null) return false;
            _root.Tick(fixedInterval);
            return true;
        }
        var top = _screens[^1].Screen;
        top.Tick(fixedInterval);
        PopExited(top);
        return true;
    }

    private void PopExited(IIndexedLoopClient screen)
    {
        // The screen may have pushed another one above itself; only a screen still on top is popped.
        if (screen.ExitRequested && _screens.Count != 0 && ReferenceEquals(_screens[^1].Screen, screen)) Pop();
    }
}

/// <summary>A screen that wants to know when the screen above it was popped (the reference's <c>State::init</c> and <c>resetAll</c>).</summary>
public interface IRevealedScreen
{
    void Revealed();
}
