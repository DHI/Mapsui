using System;
using System.Collections.Generic;

namespace Mapsui.Styles;

public class VectorStyle : BaseStyle
{
    public VectorStyle()
    {
        Outline = new Pen { Color = Color.Gray, Width = 1 };
        Line = new Pen { Color = Color.Black, Width = 1 };
        Fill = new Brush { Color = Color.White };
    }

    /// <summary>
    /// Line style for line geometries
    /// </summary>
    public Pen? Line { get; set; }

    /// <summary>
    /// Outline style for line and polygon geometries
    /// </summary>
    public Pen? Outline { get; set; }

    /// <summary>
    /// Fill style for Polygon geometries
    /// </summary>
    public Brush? Fill { get; set; }

    #region Added for MikePlus
    /// <summary>
    /// only show symbolo style at coordinates for point
    /// </summary>
    public bool OnlyShowCoordinateForPoint { get; set; } = false;
    /// <summary>
    /// style type
    /// </summary>
    public StyleTypes StyleType { get; set; } = StyleTypes.None;
    /// <summary>
    /// draw arrow for line geometries
    /// </summary>
    public bool DrawArrow { get; set; } = false;

    /// <summary>
    /// draw arrow position for line geometries
    /// </summary>
    public ArrowPosition DrawArrowPosition { get; set; } = ArrowPosition.Middle;

    /// <summary>
    /// draw image at the middle of line, image source
    /// </summary>
    public Image? Image { get; set; } = null;

    /// <summary>
    /// Class Break symbol field name
    /// </summary>
    public string? ClassBreakField { get; set; }

    /// <summary>
    /// call back to get class break field value
    /// </summary>
    public Func<IFeature, string?, bool>? ClassBreakMethod { get; set; }
    /// <summary>
    /// Class Break list
    /// </summary>
    public List<ClassBreak>? ClassBreaks { get; set; }

    /// <summary>
    /// Class Break symbol field name
    /// </summary>
    public string? UniqueValueField { get; set; }

    /// <summary>
    /// the default style of the other values
    /// </summary>
    public IStyle? OtherValueStyle { get; set; }

    /// <summary>
    /// unique value list
    /// </summary>
    public List<ValueItem>? UniqueValueItems { get; set; }

    /// <summary>
    /// call back to get unique value field value
    /// </summary>
    public Func<IFeature, string?, bool>? UniqueValueMethod { get; set; }

    [Flags]
    public enum StyleTypes
    {
        None = 0x0,
        Point = 0x1,
        Polyline = 0x2,
        Polygon = 0x4
    }

    public enum ArrowPosition
    {
        Start,
        End,
        Middle
    }

    public class ClassBreak
    {
        public double BreakValue { get; set; }
        // The corresponding style
        public object? ClassBreakStyle { get; set; } = null;
    }

    public class ValueItem
    {
        public double Value { get; set; }
        // The corresponding style
        public IStyle? ValueStyle { get; set; } = null;
    }
    #endregion
}
