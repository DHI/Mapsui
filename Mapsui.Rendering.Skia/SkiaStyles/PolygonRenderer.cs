using Mapsui.Extensions;
using Mapsui.Rendering.Caching;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Rendering.Skia.Images;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;
using System;
using System.Linq;

namespace Mapsui.Rendering.Skia;

internal static class PolygonRenderer
{
    /// <summary>
    /// fill paint scale
    /// </summary>
    private const float _scale = 10.0f;

    #region Update for MikeOlus.
    public static void Draw(SKCanvas canvas, Viewport viewport, VectorStyle vectorStyle, IFeature feature,
        Polygon polygon, float opacity, VectorCache vectorCache, int position)
    {
        // polygon - relevant for GeometryCollection children
        SKPath ToPath((long featureId, int position, MRect extent, double rotation, float lineWidth) valueTuple)
        {
            var result = polygon.ToSkiaPath(viewport, viewport.ToSkiaRect(), valueTuple.lineWidth);
            return result;
        }

        if (vectorStyle == null)
            return;

        if (feature == null)
            return;

        var extent = viewport.ToExtent();
        var rotation = viewport.Rotation;
        float lineWidth = (float)(vectorStyle.Outline?.Width ?? 1);

        using var path = vectorCache.GetOrCreate((feature.Id, position, extent, rotation, lineWidth), ToPath);
        if (vectorStyle.Fill.IsVisible())
        {
            Pen? classBreakPen = null;
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

                //If uniqueVectorStyle is null, stop drawing the polygon
                if (uniqueVectorStyle != null)
                {
                    using var fillPaintU = vectorCache.GetOrCreate((uniqueVectorStyle.Fill, opacity, viewport.Rotation), CreateSkPaint);
                    DrawPath(canvas, uniqueVectorStyle, path, fillPaintU);

                    if (uniqueVectorStyle.Outline.IsVisible())
                    {
                        using var paint = vectorCache.GetOrCreate((uniqueVectorStyle.Outline, opacity), CreateSkPaint);
                        canvas.DrawPath(path, paint);
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
                    using var fillPaintB = vectorCache.GetOrCreate((breakVectorStyle.Fill, opacity, viewport.Rotation), CreateSkPaint);
                    DrawPath(canvas, breakVectorStyle, path, fillPaintB);

                    if (breakVectorStyle.Outline.IsVisible())
                    {
                        using var paint = vectorCache.GetOrCreate((breakVectorStyle.Outline, opacity), CreateSkPaint);
                        canvas.DrawPath(path, paint);
                    }
                    return;
                }
            }

            //General style with class break Pen or not
            using var fillPaint = vectorCache.GetOrCreate((vectorStyle.Fill, opacity, viewport.Rotation), CreateSkPaint);
            if (classBreakPen != null)
            {
                var fillColor = classBreakPen.Color;
                fillPaint.Instance.Color = fillColor.ToSkia(opacity);
            }

            DrawPath(canvas, vectorStyle, path, fillPaint);
        }

        if (vectorStyle.Outline.IsVisible())
        {
            using var paint = vectorCache.GetOrCreate((vectorStyle.Outline, opacity), CreateSkPaint);
            canvas.DrawPath(path, paint);
        }
    }
    #endregion

    internal static void DrawPath(SKCanvas canvas, VectorStyle vectorStyle, CacheTracker<SKPath> path, CacheTracker<SKPaint> paintFill)
    {
        if (vectorStyle?.Fill?.FillStyle == FillStyle.Solid)
        {
            canvas.DrawPath(path, paintFill);
        }
        else
        {
            // Do this, because if not, path isn't filled complete
            using (new SKAutoCanvasRestore(canvas))
            {
                var skPath = path.Instance;
                canvas.ClipPath(skPath);
                var bounds = skPath.Bounds;
                // Make sure, that the brush starts with the correct position
                var inflate = ((int)skPath.Bounds.Width * 0.3f / _scale) * _scale;
                bounds.Inflate(inflate, inflate);
                // Draw rect with bigger size, which is clipped by path
                canvas.DrawRect(bounds, paintFill);
            }
        }
    }

    internal static SKPaint CreateSkPaint((Brush? brush, float opacity, double rotation) valueTuple, RenderService renderService)
    {
        var brush = valueTuple.brush;
        var opacity = valueTuple.opacity;
        var rotation = valueTuple.rotation;
        var fillColor = Color.Gray; // default

        var paintFill = new SKPaint { IsAntialias = true };

        if (brush?.Color is not null)
        {
            fillColor = brush.Color.Value;
        }

        // Is there a FillStyle?
        if (brush?.FillStyle == FillStyle.Solid)
        {
            paintFill.StrokeWidth = 0;
            paintFill.Style = SKPaintStyle.Fill;
            paintFill.PathEffect = null;
            paintFill.Shader = null;
            paintFill.Color = fillColor.ToSkia(opacity);
        }
        else
        {
            paintFill.StrokeWidth = 1;
            paintFill.Style = SKPaintStyle.Stroke;
            paintFill.Shader = null;
            paintFill.Color = fillColor.ToSkia(opacity);
            using var fillPath = new SKPath();
            var matrix = SKMatrix.CreateScale(_scale, _scale);

            switch (brush?.FillStyle)
            {
                case FillStyle.Cross:
                    fillPath.MoveTo(_scale * 0.8f, _scale * 0.8f);
                    fillPath.LineTo(0, 0);
                    fillPath.MoveTo(0, _scale * 0.8f);
                    fillPath.LineTo(_scale * 0.8f, 0);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.DiagonalCross:
                    fillPath.MoveTo(_scale, _scale);
                    fillPath.LineTo(0, 0);
                    fillPath.MoveTo(0, _scale);
                    fillPath.LineTo(_scale, 0);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.BackwardDiagonal:
                    fillPath.MoveTo(0, _scale);
                    fillPath.LineTo(_scale, 0);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.ForwardDiagonal:
                    fillPath.MoveTo(_scale, _scale);
                    fillPath.LineTo(0, 0);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.Dotted:
                    paintFill.Style = SKPaintStyle.StrokeAndFill;
                    fillPath.AddCircle(_scale * 0.5f, _scale * 0.5f, _scale * 0.35f);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.Horizontal:
                    fillPath.MoveTo(0, _scale * 0.5f);
                    fillPath.LineTo(_scale, _scale * 0.5f);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.Vertical:
                    fillPath.MoveTo(_scale * 0.5f, 0);
                    fillPath.LineTo(_scale * 0.5f, _scale);
                    paintFill.PathEffect = SKPathEffect.Create2DPath(matrix, fillPath);
                    break;
                case FillStyle.Bitmap:
                    paintFill.Style = SKPaintStyle.Fill;
                    var skImage = GetSKImage(renderService, brush.Image ?? throw new Exception("Image can not be null when FillStyle is Bitmap"));
                    if (skImage != null)
                        paintFill.Shader = skImage.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                    break;
                case FillStyle.BitmapRotated:
                    paintFill.Style = SKPaintStyle.Fill;
                    var skRotatedImage = GetSKImage(renderService, brush.Image ?? throw new Exception("Image can not be null when FillStyle is BitmapRotated"));
                    if (skRotatedImage != null)
                        paintFill.Shader = skRotatedImage.ToShader(SKShaderTileMode.Repeat,
                            SKShaderTileMode.Repeat,
                            SKMatrix.CreateRotation((float)(rotation * Math.PI / 180.0f),
                                skRotatedImage.Width >> 1, skRotatedImage.Height >> 1));
                    break;
                default:
                    paintFill.PathEffect = null;
                    break;
            }
        }

        return paintFill;
    }

    internal static SKPaint CreateSkPaint((Pen? pen, float opacity) valueTuple)
    {
        var pen = valueTuple.pen;
        var opacity = valueTuple.opacity;
        float lineWidth = 1;
        var lineColor = Color.Black; // default
        var strokeCap = PenStrokeCap.Butt; // default
        var strokeJoin = StrokeJoin.Miter; // default
        var strokeMiterLimit = 4f; // default
        var strokeStyle = PenStyle.Solid; // default
        float[]? dashArray = null; // default
        float dashOffset = 0; // default

        if (pen != null)
        {
            lineWidth = (float)pen.Width;
            lineColor = pen.Color;
            strokeCap = pen.PenStrokeCap;
            strokeJoin = pen.StrokeJoin;
            strokeMiterLimit = pen.StrokeMiterLimit;
            strokeStyle = pen.PenStyle;
            dashArray = pen.DashArray;
            dashOffset = pen.DashOffset;
        }

        var paintStroke = new SKPaint { IsAntialias = true };
        {
            paintStroke.Style = SKPaintStyle.Stroke;
            paintStroke.StrokeWidth = lineWidth;
            paintStroke.Color = lineColor.ToSkia(opacity);
            paintStroke.StrokeCap = strokeCap.ToSkia();
            paintStroke.StrokeJoin = strokeJoin.ToSkia();
            paintStroke.StrokeMiter = strokeMiterLimit;
            if (strokeStyle != PenStyle.Solid)
                paintStroke.PathEffect = strokeStyle.ToSkia(lineWidth, dashArray, dashOffset);
            else
                paintStroke.PathEffect = null;
        }

        return paintStroke;
    }

    private static SKImage? GetSKImage(RenderService renderService, Image image)
    {
        if (image is null)
            return null;
#pragma warning disable IDISP001 // The cache is responsible for disposing the items created in the cache.
        var drawableImage = renderService.DrawableImageCache.GetOrCreate(image.SourceId,
            () => ImageStyleRenderer.TryCreateDrawableImage(image, renderService.ImageSourceCache));
#pragma warning restore IDISP001
        if (drawableImage == null)
            return null;

        if (drawableImage is BitmapDrawableImage bitmapImage)
        {
            if (image.BitmapRegion is null)
                return bitmapImage.Image;

            var imageRegionKey = image.GetSourceIdForBitmapRegion();
#pragma warning disable IDISP001 // The cache is responsible for disposing the items created in the cache.
            var regionDrawableImage = renderService.DrawableImageCache.GetOrCreate(imageRegionKey, () => CreateBitmapImage(bitmapImage.Image, image.BitmapRegion));
#pragma warning restore IDISP001
            if (regionDrawableImage == null)
                return null;
            if (regionDrawableImage is BitmapDrawableImage regionBitmapImage)
                return regionBitmapImage.Image;
            throw new Exception("Only bitmaps are is supported for polygon fill.");
        }
        throw new Exception("Only bitmaps are is supported for polygon fill.");
    }

    private static BitmapDrawableImage CreateBitmapImage(SKImage skImage, BitmapRegion bitmapRegion)
    {
        return new BitmapDrawableImage(skImage.Subset(new SKRectI(bitmapRegion.X, bitmapRegion.Y,
            bitmapRegion.X + bitmapRegion.Width, bitmapRegion.Y + bitmapRegion.Height)));
    }
}
