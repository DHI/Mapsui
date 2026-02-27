using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Rendering.Skia.Images;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using Mapsui.Utilities;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Linq;

namespace Mapsui.Rendering.Skia;

public class SymbolStyleRenderer : ISkiaStyleRenderer, IFeatureSize
{
    #region Update for MikePlus.
    private static IFeature? _feature = null;

    public static void DrawStatic(SKCanvas canvas, Viewport viewport, ILayer layer, double x, double y, IPointStyle pointStyle, RenderService renderService, IFeature feature)
    {
        _feature = feature;
        var opacity = (float)(layer.Opacity * pointStyle.Opacity);
        PointStyleRenderer.DrawPointStyle(canvas, viewport, x, y, pointStyle, renderService, opacity, DrawSymbolStyle);
    }

    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        //Updated for MikePlus
        _feature = feature;
        var symbolStyle = (SymbolStyle)style;
        bool drawflag = !symbolStyle.OnlyShowCoordinateForPoint;
        if (symbolStyle.OnlyShowCoordinateForPoint
            && (feature is PointFeature || (feature is GeometryFeature geometryFeature && geometryFeature?.Geometry is Point)))
        {
            drawflag = true;
        }

        if (drawflag)
        {
            feature.CoordinateVisitor((x, y, setter) =>
            {
                var opacity = (float)(layer.Opacity * symbolStyle.Opacity);
                PointStyleRenderer.DrawPointStyle(canvas, viewport, x, y, symbolStyle, renderService, opacity, DrawSymbolStyle);
            });
        }

        return true;
    }

    private static void DrawSymbolStyle(SKCanvas canvas, IPointStyle pointStyle, RenderService renderService, float opacity)
    {
        if (_feature == null)
            return;
        if (pointStyle is SymbolStyle symbolStyle)
        {
            canvas.Save();

            var offset = symbolStyle.RelativeOffset.GetAbsoluteOffset(SymbolStyle.DefaultWidth, SymbolStyle.DefaultWidth);
            canvas.Translate((float)offset.X, (float)-offset.Y);

            using var path = renderService.VectorCache.GetOrCreate(symbolStyle.SymbolType, CreatePath);
            if (symbolStyle.Fill.IsVisible())
            {
                Pen? classBreakPen = null;

                //If has unique values, use unique styles first
                if (symbolStyle.UniqueValueMethod != null && symbolStyle.UniqueValueField != null
                    && symbolStyle.UniqueValueItems != null && symbolStyle.UniqueValueItems.Count > 0)
                {
                    IStyle? uniqueStyle = null;
                    bool columnSuc = false;
                    columnSuc = symbolStyle.UniqueValueMethod(_feature, symbolStyle.UniqueValueField);
                    if (!columnSuc)
                        return;

                    if (double.TryParse(_feature[symbolStyle.UniqueValueField]?.ToString(), out double val))
                    {
                        var uniqueItem = symbolStyle.UniqueValueItems.Where(cb => cb.Value == val);
                        if (uniqueItem != null && uniqueItem.Count() > 0 && uniqueItem.First() != null
                            && uniqueItem.First().ValueStyle is StyleCollection coll)
                        {
                            uniqueStyle = coll.Styles.First();
                        }

                        //It means the value has no unique style, then use default style, if default style is setted
                        if (uniqueStyle == null && symbolStyle.OtherValueStyle != null
                            && symbolStyle.OtherValueStyle is StyleCollection coll2 && coll2.Styles.Count > 0
                            && coll2.Styles.First() is VectorStyle defaultStyle)
                            uniqueStyle = defaultStyle;
                    }

                    //If uniqueStyle is null or not vectoeStyle or ImageStyle, stop drawing the point
                    if (uniqueStyle != null)
                    {
                        //case 1: vector style
                        if (uniqueStyle is SymbolStyle uniqueVectorStyle)
                        {
                            using var fillPaint = renderService.VectorCache.GetOrCreate((uniqueVectorStyle.Fill!, opacity), CreateFillPaint);
                            using var pathU = renderService.VectorCache.GetOrCreate(uniqueVectorStyle.SymbolType, CreatePath);
                            canvas.DrawPath(pathU, fillPaint);

                            if (uniqueVectorStyle.Outline.IsVisible())
                            {
                                using var linePaint = renderService.VectorCache.GetOrCreate((uniqueVectorStyle.Outline!, opacity), CreateLinePaint);
                                canvas.DrawPath(pathU, linePaint);
                            }
                        }

                        //case 2: image source
                        if (uniqueStyle is ImageStyle uniqueImageStyle)
                        {
                            using var pathI = renderService.VectorCache.GetOrCreate(symbolStyle.SymbolType, CreatePath);
                            DrawImage(canvas, pathI.Instance, uniqueImageStyle.Image, renderService, opacity);
                        }
                    }

                    canvas.Restore();
                    return;
                }

                //If  has class breaks, use break styles secondly
                if (symbolStyle.ClassBreakMethod != null && symbolStyle.ClassBreakField != null
                        && symbolStyle.ClassBreaks != null && symbolStyle.ClassBreaks.Count > 0)
                {
                    IStyle? breakVectorStyle = null;
                    bool columnSuc = false;
                    columnSuc = symbolStyle.ClassBreakMethod(_feature, symbolStyle.ClassBreakField);
                    if (columnSuc)
                    {
                        if (double.TryParse(_feature[symbolStyle.ClassBreakField]?.ToString(), out double val))
                        {
                            var sortedBreaks = symbolStyle.ClassBreaks.OrderBy(cb => cb.BreakValue).ToList();

                            for (int i = 0; i < sortedBreaks.Count - 1; i++)
                            {
                                var currentBreak = sortedBreaks[i];
                                var nextBreak = sortedBreaks[i + 1];
                                if (val >= currentBreak.BreakValue && val < nextBreak.BreakValue)
                                {
                                    //case 1: entire vector style for Type: Range values
                                    if (currentBreak.ClassBreakStyle is StyleCollection coll1)
                                    {
                                        breakVectorStyle = coll1.Styles.FirstOrDefault();
                                    }
                                    //case 2: only Pen with color and width for Type: Graduated color and Graduated size
                                    else if (currentBreak.ClassBreakStyle is Pen pen1)
                                    {
                                        classBreakPen = pen1;
                                    }
                                    break;
                                }
                            }

                            //use last style as default
                            if (breakVectorStyle == null && sortedBreaks.Last().ClassBreakStyle is StyleCollection coll)
                            {
                                breakVectorStyle = coll.Styles.FirstOrDefault();
                            }
                            if (classBreakPen == null && sortedBreaks.Last().ClassBreakStyle is Pen pen)
                            {
                                classBreakPen = pen;
                            }
                        }
                    }

                    if (breakVectorStyle == null && classBreakPen == null)
                    {
                        canvas.Restore();
                        return;
                    }

                    if (breakVectorStyle != null)
                    {
                        //case 1: vector style
                        if (breakVectorStyle is SymbolStyle breakSymbolStyle)
                        {
                            using var pathB = renderService.VectorCache.GetOrCreate(breakSymbolStyle.SymbolType, CreatePath);
                            if (breakSymbolStyle.Outline.IsVisible())
                            {
                                using var linePaint = renderService.VectorCache.GetOrCreate((breakSymbolStyle.Outline!, opacity), CreateLinePaint);
                                canvas.DrawPath(pathB, linePaint);
                            }

                            using var fillPaint = renderService.VectorCache.GetOrCreate((breakSymbolStyle.Fill!, opacity), CreateFillPaint);
                            canvas.DrawPath(pathB, fillPaint);
                        }

                        //case 2: image source
                        if (breakVectorStyle is ImageStyle breakImageStyle)
                        {
                            using var pathI = renderService.VectorCache.GetOrCreate(symbolStyle.SymbolType, CreatePath);
                            DrawImage(canvas, pathI.Instance, breakImageStyle.Image, renderService, opacity);
                        }

                        canvas.Restore();
                        return;
                    }
                }

                //General style with class break Pen or not
                using var fillPaintG = renderService.VectorCache.GetOrCreate((symbolStyle.Fill!, opacity), CreateFillPaint);
                if (classBreakPen != null)
                {
                    var fillColor = classBreakPen.Color;
                    fillPaintG.Instance.Color = fillColor.ToSkia(opacity);
                    fillPaintG.Instance.StrokeWidth = (float)classBreakPen.Width;
                }

                canvas.DrawPath(path, fillPaintG);
            }

            if (symbolStyle.Outline.IsVisible())
            {
                using var linePaint = renderService.VectorCache.GetOrCreate((symbolStyle.Outline!, opacity), CreateLinePaint);
                canvas.DrawPath(path, linePaint);
            }

            canvas.Restore();
        }
        else
            throw new ArgumentException($"Expected {nameof(SymbolStyle)} but got {pointStyle?.GetType().Name}");
    }
    #endregion

    #region Added for MikePlus.
    /// <summary>
    /// Draw image at the point(support Bitmap/SVG)
    /// </summary>
    private static void DrawImage(SKCanvas canvas, SKPath symbolPath, Image? image, RenderService renderService, float opacity)
    {
        if (image == null || renderService == null || canvas == null)
            return;

#pragma warning disable IDISP001
        var drawableImage = renderService.DrawableImageCache.GetOrCreate(image.SourceId,
            () => ImageStyleRenderer.TryCreateDrawableImage(image, renderService.ImageSourceCache));
#pragma warning restore IDISP001

        if (drawableImage == null)
            return;

        var pathBounds = symbolPath.Bounds;
        var centerX = pathBounds.MidX;
        var centerY = pathBounds.MidY;

        canvas.Save();

        try
        {
            canvas.Translate(centerX, centerY);

            if (drawableImage is BitmapDrawableImage bitmapDrawable)
            {
                DrawBitmapImage(canvas, bitmapDrawable.Image, image, opacity);
            }
            else if (drawableImage is SvgDrawableImage svgImage)
            {
                if (image.SvgFillColor.HasValue || image.SvgStrokeColor.HasValue)
                {
                    var key = image.GetSourceIdForSvgWithCustomColors();
#pragma warning disable IDISP001
                    var coloredDrawableImage = renderService.DrawableImageCache.GetOrCreate(key,
                        () => CreateCustomColoredSvg(image, svgImage));
#pragma warning restore IDISP001

                    if (coloredDrawableImage is SvgDrawableImage customColoredSvgImage)
                    {
                        DrawSKPicture(canvas, customColoredSvgImage.Picture, opacity, image.BlendModeColor);
                    }
                    else if (coloredDrawableImage is BitmapDrawableImage coloredBitmap)
                    {
                        DrawSKImage(canvas, coloredBitmap.Image, opacity);
                    }
                }
                else
                {
                    DrawSKPicture(canvas, svgImage.Picture, opacity, image.BlendModeColor);
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// draw Bitmap
    /// </summary>
    private static void DrawBitmapImage(SKCanvas canvas, SKImage skImage, Image image, float opacity)
    {
        if (skImage == null)
            return;

        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(255 * opacity)),
            IsAntialias = true,
        };

        if (image.BitmapRegion != null)
        {
            using var croppedImage = skImage.Subset(new SKRectI(
                image.BitmapRegion.X,
                image.BitmapRegion.Y,
                image.BitmapRegion.X + image.BitmapRegion.Width,
                image.BitmapRegion.Y + image.BitmapRegion.Height));
            DrawCenteredBitmap(canvas, croppedImage, paint);
        }
        else
        {
            DrawCenteredBitmap(canvas, skImage, paint);
        }
    }

    private static readonly SKSamplingOptions _skSamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.None);
    private static void DrawSKImage(SKCanvas canvas, SKImage bitmap, float opacity)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(255 * opacity)),
            IsAntialias = true
        };

        var halfWidth = bitmap.Width >> 1;
        var halfHeight = bitmap.Height >> 1;
        var rect = new SKRect(-halfWidth, -halfHeight, halfWidth, halfHeight);

        canvas.DrawImage(bitmap, rect, _skSamplingOptions, paint);
    }
    /// <summary>
    /// draw Bitmap
    /// </summary>
    private static void DrawCenteredBitmap(SKCanvas canvas, SKImage skImage, SKPaint paint)
    {
        var halfWidth = skImage.Width / 2f;
        var halfHeight = skImage.Height / 2f;

        var drawRect = new SKRect(-halfWidth, -halfHeight, halfWidth, halfHeight);

        canvas.DrawImage(skImage, drawRect, _skSamplingOptions, paint);
    }

    private static void DrawSKPicture(SKCanvas canvas, SKPicture picture, float opacity, Color? blendModeColor)
    {
        using var skPaint = CreatePaintForSKPicture(opacity, blendModeColor);

        var halfWidth = picture.CullRect.Width / 2;
        var halfHeight = picture.CullRect.Height / 2;
        var matrix = SKMatrix.CreateTranslation(-halfWidth, -halfHeight);

        canvas.DrawPicture(picture, in matrix, skPaint);
    }

    private static SKPaint CreatePaintForSKPicture(float opacity, Color? blendModeColor)
    {
        var paint = new SKPaint { IsAntialias = true };

        if (blendModeColor is not null)
            paint.ColorFilter = SKColorFilter.CreateBlendMode(blendModeColor.ToSkia(opacity), SKBlendMode.SrcIn);

        if (Math.Abs(opacity - 1) > Constants.Epsilon)
            paint.Color = new SKColor(255, 255, 255, (byte)(255 * opacity));

        return paint;
    }

    private static IDrawableImage CreateCustomColoredSvg(Image image, SvgDrawableImage originalSvgImage)
    {
        var originalStream = originalSvgImage.OriginalStream ?? throw new NullReferenceException("Original Stream is null");
        using var modifiedSvgStream = SvgColorModifier.GetModifiedSvg(originalStream, image.SvgFillColor, image.SvgStrokeColor);
#pragma warning disable IDISP004
        var skSvg = new SKSvg();
        modifiedSvgStream.Position = 0;
        skSvg.Load(modifiedSvgStream);
#pragma warning restore IDISP004

        if (skSvg.Picture is null)
            throw new Exception("Failed to load modified SVG picture.");

        if (image.RasterizeSvg)
        {
            if (image.BlendModeColor is not null)
                throw new NotSupportedException("BlendModeColor is not supported for rasterized SVGs.");

            var result = new BitmapDrawableImage(ConvertPictureToImage(skSvg.Picture,
                (int)skSvg.Picture.CullRect.Width, (int)skSvg.Picture.CullRect.Height));
            skSvg.Dispose();
            return result;
        }

        return new SvgDrawableImage(skSvg);
    }
    public static SKImage ConvertPictureToImage(SKPicture picture, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(picture);
        return surface.Snapshot();
    }
    #endregion

    private static SKPath CreatePath(SymbolType symbolType)
    {
        var width = (float)SymbolStyle.DefaultWidth;
        var halfWidth = width / 2;
        var halfHeight = (float)SymbolStyle.DefaultHeight / 2;
        var skPath = new SKPath();

        switch (symbolType)
        {
            case SymbolType.Ellipse:
                skPath.AddCircle(0, 0, halfWidth);
                break;
            case SymbolType.Rectangle:
                skPath.AddRect(new SKRect(-halfWidth, -halfHeight, halfWidth, halfHeight));
                break;
            case SymbolType.Triangle:
                TrianglePath(skPath, 0, 0, width);
                break;
            default: // Invalid value
                throw new ArgumentException($"Unknown {nameof(SymbolType)} '{nameof(symbolType)}'");
        }

        return skPath;
    }

    private static SKPaint CreateLinePaint((Pen outline, float opacity) valueTuple)
    {
        var outline = valueTuple.outline;
        var opacity = valueTuple.opacity;

        return new SKPaint
        {
            Color = outline.Color.ToSkia(opacity),
            StrokeWidth = (float)outline.Width,
            StrokeCap = outline.PenStrokeCap.ToSkia(),
            PathEffect = outline.PenStyle.ToSkia((float)outline.Width),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
    }

    private static SKPaint CreateFillPaint((Brush fill, float opacity) valueTuple)
    {
        var fill = valueTuple.fill;
        var opacity = valueTuple.opacity;

        return new SKPaint
        {
            Color = fill.Color.ToSkia(opacity),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
    }

    /// Triangle of side 'sideLength', centered on the same point as if a circle of diameter 'sideLength' was there
    private static void TrianglePath(SKPath path, float x, float y, float sideLength)
    {
        var altitude = Math.Sqrt(3) / 2.0 * sideLength;
        var inradius = altitude / 3.0;
        var circumradius = 2.0 * inradius;

        var topX = x;
        var topY = y - circumradius;
        var leftX = x + sideLength * -0.5;
        var leftY = y + inradius;
        var rightX = x + sideLength * 0.5;
        var rightY = y + inradius;

        path.MoveTo(topX, (float)topY);
        path.LineTo((float)leftX, (float)leftY);
        path.LineTo((float)rightX, (float)rightY);
        path.Close();
    }

    bool IFeatureSize.NeedsFeature => false;

    double IFeatureSize.FeatureSize(IStyle style, RenderService renderService, IFeature? feature)
    {
        if (style is SymbolStyle symbolStyle)
        {
            return FeatureSize(symbolStyle);
        }

        return 0;
    }

    public static double FeatureSize(SymbolStyle symbolStyle)
    {
        var vectorSize = VectorStyleRenderer.FeatureSize(symbolStyle);
        Size symbolSize = new Size(vectorSize, vectorSize);

        var size = Math.Max(symbolSize.Height, symbolSize.Width);
        size *= symbolStyle.SymbolScale; // Symbol Scale
        size = Math.Max(size, SymbolStyle.DefaultWidth); // if defaultWith is larger take this.

        // Calc offset (relative or absolute)
        var offset = symbolStyle.Offset.Combine(symbolStyle.RelativeOffset.GetAbsoluteOffset(symbolSize.Width, symbolSize.Height));

        // Pythagoras for maximal distance
        var length = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y);

        // add length to size multiplied by two because the total size increased by the offset
        size += length * 2;

        return size;
    }
}
