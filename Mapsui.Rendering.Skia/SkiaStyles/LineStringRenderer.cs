using Mapsui.Extensions;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Rendering.Skia.Images;
using Mapsui.Styles;
using Mapsui.Utilities;
using NetTopologySuite.Geometries;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Generic;
using System.Linq;
using static Mapsui.Styles.VectorStyle;
using Image = Mapsui.Styles.Image;

namespace Mapsui.Rendering.Skia;

public static class LineStringRenderer
{
    #region Updated for MikePlus
    public static void Draw(SKCanvas canvas, Viewport viewport, VectorStyle? vectorStyle,
        IFeature feature, LineString lineString, float opacity, RenderService renderService, int position)
    {
        if (vectorStyle == null)
            return;

        // lineString - relevant for GeometryCollection children
        SKPath ToPath((long featureId, int position, MRect extent, double rotation, float lineWidth) valueTuple)
        {
            var result = lineString.ToSkiaPath(viewport, viewport.ToSkiaRect(), valueTuple.lineWidth);
            _ = result.Bounds;
            _ = result.TightBounds;
            return result;
        }

        var extent = viewport.ToExtent();
        var rotation = viewport.Rotation;
        var lineWidth = (float)(vectorStyle.Line?.Width ?? 1f);

        if (vectorStyle.Line.IsVisible())
        {
            Pen? classBreakPen = null;

            using var path = renderService.VectorCache.GetOrCreate((feature.Id, position, extent, rotation, lineWidth), ToPath);

            //If has unique values, use unique styles first
            if (vectorStyle.UniqueValueMethod != null && vectorStyle.UniqueValueField != null
                && vectorStyle.UniqueValueItems != null && vectorStyle.UniqueValueItems.Count > 0)
            {
                VectorStyle? uniqueVectorStyle = null;
                bool columnSuc = false;
                columnSuc = vectorStyle.UniqueValueMethod(feature, vectorStyle.UniqueValueField);
                if (columnSuc)
                {
                    if (double.TryParse(feature[vectorStyle.UniqueValueField]?.ToString(), out double val))
                    {
                        var uniqueItem = vectorStyle.UniqueValueItems.Where(cb => cb.Value == val);
                        if (uniqueItem != null && uniqueItem.First() != null
                            && uniqueItem.First().ValueStyle is StyleCollection coll
                            && coll.Styles.First() is VectorStyle vector)
                        {
                            uniqueVectorStyle = vector;
                        }

                        //It means the value has no unique style, then use default style, if default style is setted
                        if (uniqueVectorStyle == null && vectorStyle.OtherValueStyle != null
                            && vectorStyle.OtherValueStyle is StyleCollection coll2
                            && coll2.Styles.First() is VectorStyle defaultStyle)
                            uniqueVectorStyle = defaultStyle;
                    }
                }

                //If uniqueVectorStyle is null, stop drawing the Line String
                if (uniqueVectorStyle != null)
                {
                    // If the Outline property is set and has a width greater than 0, draw the outline first.
                    if (uniqueVectorStyle.Outline?.Width > 0)
                    {
                        // The width is calculated as the sum of the outline width and the line width, if both are defined.
                        // For the caching callback to work, the calculated width must be passed to the CreateSkPaint method.
                        var width = uniqueVectorStyle.Outline.Width + uniqueVectorStyle.Outline.Width + uniqueVectorStyle.Line?.Width ?? 1;
                        using var paintOutline = renderService.VectorCache.GetOrCreate((uniqueVectorStyle.Outline, (float?)width, opacity), CreateSkPaint);
                        canvas.DrawPath(path, paintOutline);
                    }

                    using var paintLineU = renderService.VectorCache.GetOrCreate((uniqueVectorStyle.Line, (float?)null, opacity), CreateSkPaint);
                    canvas.DrawPath(path, paintLineU);

                    //draw arrow
                    if (uniqueVectorStyle.DrawArrow)
                    {
                        DrawArrow(canvas, path.Instance, viewport, uniqueVectorStyle, opacity, renderService);
                    }

                    //draw image with rotation at the middle point
                    if (uniqueVectorStyle.Image != null)
                    {
                        DrawMiddleImage(canvas, path.Instance, uniqueVectorStyle.Image, renderService, opacity);
                    }
                }

                return;
            }

            //If  has class breaks, use break styles secondly
            if (vectorStyle.ClassBreakMethod != null && vectorStyle.ClassBreakField != null
                    && vectorStyle.ClassBreaks != null && vectorStyle.ClassBreaks.Count > 0)
            {
                VectorStyle? breakVectorStyle = null;
                bool columnSuc = false;
                columnSuc = vectorStyle.ClassBreakMethod(feature, vectorStyle.ClassBreakField);
                if (columnSuc)
                {
                    if (double.TryParse(feature[vectorStyle.ClassBreakField]?.ToString(), out double val))
                    {
                        var sortedBreaks = vectorStyle.ClassBreaks.OrderBy(cb => cb.BreakValue).ToList();

                        for (int i = 0; i < sortedBreaks.Count - 1; i++)
                        {
                            var currentBreak = sortedBreaks[i];
                            var nextBreak = sortedBreaks[i + 1];
                            if (val >= currentBreak.BreakValue && val < nextBreak.BreakValue)
                            {
                                //case 1: entire vector style for Type: Range values
                                if (currentBreak.ClassBreakStyle is StyleCollection coll1 && coll1.Styles.First() is VectorStyle vector1)
                                {
                                    breakVectorStyle = vector1;
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
                        if (breakVectorStyle == null && sortedBreaks.Last().ClassBreakStyle is StyleCollection coll && coll.Styles.First() is VectorStyle vector)
                        {
                            breakVectorStyle = vector;
                        }
                        if (classBreakPen == null && sortedBreaks.Last().ClassBreakStyle is Pen pen)
                        {
                            classBreakPen = pen;
                        }
                    }
                }

                if (breakVectorStyle == null && classBreakPen == null)
                    return;

                if (breakVectorStyle != null)
                {
                    // If the Outline property is set and has a width greater than 0, draw the outline first.
                    if (breakVectorStyle!.Outline?.Width > 0)
                    {
                        // The width is calculated as the sum of the outline width and the line width, if both are defined.
                        // For the caching callback to work, the calculated width must be passed to the CreateSkPaint method.
                        var width = breakVectorStyle.Outline.Width + breakVectorStyle.Outline.Width + breakVectorStyle.Line?.Width ?? 1;
                        using var paintOutline = renderService.VectorCache.GetOrCreate((breakVectorStyle.Outline, (float?)width, opacity), CreateSkPaint);
                        canvas.DrawPath(path, paintOutline);
                    }

                    using var paintLineC = renderService.VectorCache.GetOrCreate((breakVectorStyle.Line, (float?)null, opacity), CreateSkPaint);
                    canvas.DrawPath(path, paintLineC);

                    //draw arrow
                    if (breakVectorStyle.DrawArrow)
                    {
                        DrawArrow(canvas, path.Instance, viewport, breakVectorStyle, opacity, renderService);
                    }

                    //draw image with rotation at the middle point
                    if (breakVectorStyle.Image != null)
                    {
                        DrawMiddleImage(canvas, path.Instance, breakVectorStyle.Image, renderService, opacity);
                    }
                    return;
                }
            }

            //General style with class break Pen or not
            // If the Outline property is set and has a width greater than 0, draw the outline first.
            if (vectorStyle.Outline?.Width > 0)
            {
                // The width is calculated as the sum of the outline width and the line width, if both are defined.
                // For the caching callback to work, the calculated width must be passed to the CreateSkPaint method.
                var width = vectorStyle.Outline.Width + vectorStyle.Outline.Width + vectorStyle.Line?.Width ?? 1;
                using var paintOutline = renderService.VectorCache.GetOrCreate((vectorStyle.Outline, (float?)width, opacity), CreateSkPaint);
                canvas.DrawPath(path, paintOutline);
            }

            using var paintLine = renderService.VectorCache.GetOrCreate((vectorStyle.Line, (float?)null, opacity), CreateSkPaint);

            if (classBreakPen != null)
            {
                var lineColor = classBreakPen.Color;
                paintLine.Instance.Color = lineColor.ToSkia(opacity);
                paintLine.Instance.StrokeWidth = (float)classBreakPen.Width;
            }

            canvas.DrawPath(path, paintLine);

            //draw arrow
            if (vectorStyle.DrawArrow)
            {
                DrawArrow(canvas, path.Instance, viewport, vectorStyle, opacity, renderService);
            }

            //draw image with rotation at the middle point
            if (vectorStyle.Image != null)
            {
                DrawMiddleImage(canvas, path.Instance, vectorStyle.Image, renderService, opacity);
            }
        }
    }
    #endregion

    #region Added for MikePlus
    private static void DrawArrow(SKCanvas canvas, SKPath path, Viewport viewport, VectorStyle style, float opacity, RenderService renderService)
    {
        if (path.PointCount < 2) return;

        SKPoint targetPoint, prevPoint;

        //cal total length
        float totalLength = CalculatePolylineTotalLength(path);
        // arrow offset along the line: 5%
        float offsetLength = totalLength * 0.05f;

        switch (style.DrawArrowPosition)
        {
            case ArrowPosition.Start:
                (targetPoint, prevPoint) = GetPointAlongPolyline(path, offsetLength);
                break;
            case ArrowPosition.Middle:
                (targetPoint, prevPoint) = GetPolylineMiddlePointAndDirection(path);
                break;
            case ArrowPosition.End:
            default:
                (targetPoint, prevPoint) = GetPointAlongPolyline(path, totalLength - offsetLength);
                break;
        }

        var dx = targetPoint.X - prevPoint.X;
        var dy = targetPoint.Y - prevPoint.Y;
        var length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.1) return;

        var nx = dx / length;
        var ny = dy / length;

        // additional: arrow size
        var arrowSize = 10f; //style.ArrowSize > 0 ? style.ArrowSize : 10f; 

        var perpX = -ny * arrowSize;
        var perpY = nx * arrowSize;

        //generate the arrow path
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(targetPoint);
        arrowPath.LineTo(targetPoint.X - nx * arrowSize - perpX / 2, targetPoint.Y - ny * arrowSize - perpY / 2);
        arrowPath.LineTo(targetPoint.X - nx * arrowSize + perpX / 2, targetPoint.Y - ny * arrowSize + perpY / 2);
        arrowPath.Close();

        var arrowPen = new Pen(style.Line?.Color ?? Color.Red, arrowSize)
        {
            PenStyle = PenStyle.Solid,
        };
        using var arrowPaint = renderService.VectorCache.GetOrCreate((arrowPen, (float?)null, opacity), CreateSkPaint);
        // fill the arrow color
        arrowPaint.Instance.Style = SKPaintStyle.Fill;
        arrowPaint.Instance.Color = style.Line?.Color.ToSkia(opacity) ?? SKColors.Red;

        // paint the arrow
        canvas.DrawPath(arrowPath, arrowPaint.Instance);
    }

    private static void DrawMiddleImage(SKCanvas canvas, SKPath path, Image middleImage,
            RenderService renderService, float baseOpacity)
    {
        if (path == null || path.PointCount < 2) return;

        // get middle point segment rotation
        var (middlePoint, directionPoint) = GetPolylineMiddlePointAndDirection(path);
        float dirX = middlePoint.X - directionPoint.X;
        float dirY = middlePoint.Y - directionPoint.Y;
        float rotationRadians = (float)Math.Atan2(dirY, dirX);
        float rotationDegrees = (float)(rotationRadians * 180 / Math.PI);

#pragma warning disable IDISP001 // The cache is responsible for disposing the items created in the cache.
        var drawableImage = renderService.DrawableImageCache.GetOrCreate(middleImage.SourceId,
            () => ImageStyleRenderer.TryCreateDrawableImage(middleImage, renderService.ImageSourceCache));
#pragma warning restore IDISP001
        if (drawableImage == null)
        {
            return;
        }

        canvas.Save();
        try
        {
            canvas.Translate(middlePoint.X, middlePoint.Y);

            canvas.RotateDegrees(rotationDegrees);

            if (drawableImage is BitmapDrawableImage bitmapImage)
            {
                if (middleImage.BitmapRegion is not null)
                {
                    var key = middleImage.GetSourceIdForBitmapRegion();
#pragma warning disable IDISP001 // The cache is responsible for disposing the items created in the cache.
#pragma warning disable IDISP003 // The cache is responsible for disposing the items created in the cache.
                    if (renderService.DrawableImageCache.GetOrCreate(key,
                        () => CreateBitmapImageForRegion(bitmapImage, middleImage.BitmapRegion)) is BitmapDrawableImage bitmapRegionImage)
                    {
                        bitmapImage = bitmapRegionImage;
#pragma warning restore IDISP001 // The cache is responsible for disposing the items created in the cache.
#pragma warning restore IDISP003 // The cache is responsible for disposing the items created in the cache.

                    }

                }
                DrawSKImage(canvas, bitmapImage.Image, baseOpacity);
            }
            else if (drawableImage is SvgDrawableImage svgImage)
            {
                if (middleImage.SvgFillColor.HasValue || middleImage.SvgStrokeColor.HasValue)
                {
                    var key = middleImage.GetSourceIdForSvgWithCustomColors();
#pragma warning disable IDISP001
                    var coloredDrawableImage = renderService.DrawableImageCache.GetOrCreate(key,
                        () => CreateCustomColoredSvg(middleImage, svgImage));
#pragma warning restore IDISP001

                    if (coloredDrawableImage is SvgDrawableImage customColoredSvgImage)
                    {
                        DrawSKPicture(canvas, customColoredSvgImage.Picture, baseOpacity, middleImage.BlendModeColor);
                    }
                    else if (coloredDrawableImage is BitmapDrawableImage coloredBitmap)
                    {
                        DrawSKImage(canvas, coloredBitmap.Image, baseOpacity);
                    }
                }
                else
                {
                    DrawSKPicture(canvas, svgImage.Picture, baseOpacity, middleImage.BlendModeColor);
                }
            }
        }
        finally
        {
            canvas.Restore();
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

    private static BitmapDrawableImage CreateBitmapImageForRegion(BitmapDrawableImage bitmapImage, BitmapRegion sprite)
    {
        return new BitmapDrawableImage(bitmapImage.Image.Subset(new SKRectI(
            sprite.X, sprite.Y, sprite.X + sprite.Width, sprite.Y + sprite.Height)));
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

    private static float CalculatePolylineTotalLength(SKPath path)
    {
        float totalLength = 0;
        for (int i = 0; i < path.PointCount - 1; i++)
        {
            var p1 = path.GetPoint(i);
            var p2 = path.GetPoint(i + 1);
            totalLength += (float)Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }
        return totalLength;
    }

    // get the position of arrow even multiple line in the polyline
    private static (SKPoint point, SKPoint directionPoint) GetPointAlongPolyline(SKPath path, float targetLength)
    {
        float currentLength = 0;
        for (int i = 0; i < path.PointCount - 1; i++)
        {
            var p1 = path.GetPoint(i);
            var p2 = path.GetPoint(i + 1);
            var segmentLen = (float)Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));

            // target length in the current segment
            if (currentLength + segmentLen >= targetLength)
            {
                float remaining = targetLength - currentLength;
                float ratio = remaining / segmentLen;

                // cal the target point
                var targetPoint = new SKPoint(
                    p1.X + ratio * (p2.X - p1.X),
                    p1.Y + ratio * (p2.Y - p1.Y)
                );

                // cal the point direction
                var directionPoint = new SKPoint(
                    targetPoint.X - 0.1f * (p2.X - p1.X),
                    targetPoint.Y - 0.1f * (p2.Y - p1.Y)
                );

                return (targetPoint, directionPoint);
            }

            currentLength += segmentLen;
        }

        // avoid exception: return the end point
        var lastPoint = path.GetPoint(path.PointCount - 1);
        var prevLastPoint = path.GetPoint(path.PointCount - 2);
        return (lastPoint, prevLastPoint);
    }

    private static (SKPoint middlePoint, SKPoint directionPoint) GetPolylineMiddlePointAndDirection(SKPath path)
    {
        // cal segment length and total length
        var segmentLengths = new List<float>();
        float totalLength = 0;
        for (int i = 0; i < path.PointCount - 1; i++)
        {
            var p1 = path.GetPoint(i);
            var p2 = path.GetPoint(i + 1);
            var segLen = (float)Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
            segmentLengths.Add(segLen);
            totalLength += segLen;
        }

        // find the position of 50% total length
        float targetLength = totalLength / 2;
        float currentLength = 0;
        int targetSegmentIndex = 0;
        for (int i = 0; i < segmentLengths.Count; i++)
        {
            if (currentLength + segmentLengths[i] >= targetLength)
            {
                targetSegmentIndex = i;
                break;
            }
            currentLength += segmentLengths[i];
        }

        // cal the target segment middle point and direction
        var pStart = path.GetPoint(targetSegmentIndex);
        var pEnd = path.GetPoint(targetSegmentIndex + 1);
        float remainingLength = targetLength - currentLength;
        float segmentLen = segmentLengths[targetSegmentIndex];
        float ratio = remainingLength / segmentLen;

        var middlePoint = new SKPoint(
            pStart.X + ratio * (pEnd.X - pStart.X),
            pStart.Y + ratio * (pEnd.Y - pStart.Y)
        );

        var directionPoint = new SKPoint(
            middlePoint.X - 0.1f * (pEnd.X - pStart.X),
            middlePoint.Y - 0.1f * (pEnd.Y - pStart.Y)
        );

        return (middlePoint, directionPoint);
    }
    #endregion

    private static SKPaint CreateSkPaint((Pen? pen, float? width, float opacity) valueTuple)
    {
        var pen = valueTuple.pen;
        var opacity = valueTuple.opacity;

        float lineWidth = valueTuple.width ?? 1;
        var lineColor = new Color();

        var strokeCap = PenStrokeCap.Butt;
        var strokeJoin = StrokeJoin.Miter;
        var strokeMiterLimit = 4f;
        var strokeStyle = PenStyle.Solid;
        float[]? dashArray = null;
        float dashOffset = 0;

        if (pen != null)
        {
            lineWidth = valueTuple.width ?? (float)pen.Width;
            lineColor = pen.Color;
            strokeCap = pen.PenStrokeCap;
            strokeJoin = pen.StrokeJoin;
            strokeMiterLimit = pen.StrokeMiterLimit;
            strokeStyle = pen.PenStyle;
            dashArray = pen.DashArray;
            dashOffset = pen.DashOffset;
        }

        var paint = new SKPaint { IsAntialias = true };
        paint.IsStroke = true;
        paint.StrokeWidth = lineWidth;
        paint.Color = lineColor.ToSkia(opacity);
        paint.StrokeCap = strokeCap.ToSkia();
        paint.StrokeJoin = strokeJoin.ToSkia();
        paint.StrokeMiter = strokeMiterLimit;
        paint.PathEffect = strokeStyle != PenStyle.Solid
            ? strokeStyle.ToSkia(lineWidth, dashArray, dashOffset)
            : null;
        return paint;
    }
}
