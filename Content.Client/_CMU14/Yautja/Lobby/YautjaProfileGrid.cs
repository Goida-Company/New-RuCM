using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._CMU14.Yautja.Lobby;

/// <summary>
/// Wraps selector cards while measuring, so enclosing panels include every row.
/// Uses the width left after the scroll bar and panel margins, not the page width.
/// </summary>
public sealed class YautjaProfileGrid : GridContainer
{
    private readonly int _preferredColumns;

    public YautjaProfileGrid(int preferredColumns = 4)
    {
        _preferredColumns = preferredColumns;
        HSeparationOverride = 8;
        VSeparationOverride = 8;
        HorizontalExpand = true;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var columns = float.IsPositiveInfinity(availableSize.X)
            ? _preferredColumns
            : YautjaProfileEditorLayout.GetResponsiveColumnCount(availableSize.X, _preferredColumns);
        if (Columns != columns)
            Columns = columns;

        return base.MeasureOverride(availableSize);
    }
}
