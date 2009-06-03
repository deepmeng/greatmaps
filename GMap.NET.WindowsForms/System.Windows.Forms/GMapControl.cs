﻿
namespace System.Windows.Forms
{
   using System.Collections.ObjectModel;
   using System.ComponentModel;
   using System.Drawing;
   using System.IO;
   using GMap.NET;
   using GMap.NET.Internals;
   using GMap.NET.WindowsForms;

   /// <summary>
   /// GMap.NET control for Windows Forms
   /// </summary>
   public partial class GMapControl : UserControl, IGControl
   {
      /// <summary>
      /// occurs when clicked on marker
      /// </summary>
      public event MarkerClick OnMarkerClick;

      /// <summary>
      /// occurs on mouse enters marker area
      /// </summary>
      public event MarkerEnter OnMarkerEnter;

      /// <summary>
      /// occurs on mouse leaves marker area
      /// </summary>
      public event MarkerLeave OnMarkerLeave;

      /// <summary>
      /// occurs on empty tile displayed
      /// </summary>
      public event EmptyTileError OnEmptyTileError;

      /// <summary>
      /// list of overlays, should be thread safe
      /// </summary>
      public readonly ObservableCollectionThreadSafe<GMapOverlay> Overlays = new ObservableCollectionThreadSafe<GMapOverlay>();

      /// <summary>
      /// max zoom
      /// </summary>
      public int MaxZoom;

      /// <summary>
      /// min zoom
      /// </summary>
      public int MinZoom;

      /// <summary>
      /// text on empty tiles
      /// </summary>
      public string EmptyTileText = "We are sorry, but we don't\nhave imagery at this zoom\nlevel for this region.";

      /// <summary>
      /// pen for empty tile borders
      /// </summary>
      public Pen EmptyTileBorders = new Pen(Brushes.White, 1);

      /// <summary>
      /// /// <summary>
      /// pen for empty tile background
      /// </summary>
      public Brush EmptytileBrush = Brushes.Navy;

      /// <summary>
      /// go to current position and center mouse OnMouseWheel
      /// </summary>
      bool CenterPositionOnMouseWheel = true;

      // internal stuff
      internal readonly Core Core = new Core();
      internal readonly Font CopyrightFont = new Font(FontFamily.GenericSansSerif, 7, FontStyle.Regular);
      internal readonly Font MissingDataFont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold);
      internal readonly StringFormat CenterFormat = new StringFormat();
      bool RaiseEmptyTileError = false;

      /// <summary>
      /// construct
      /// </summary>
      public GMapControl()
      {
         if(!DesignModeInConstruct)
         {
            Purity.Instance.ImageProxy = new WindowsFormsImageProxy();

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.Opaque, true);

            // to know when to invalidate
            Core.OnNeedInvalidation += new NeedInvalidation(Core_OnNeedInvalidation);
            Core.OnMapDrag += new MapDrag(GMap_OnMapDrag);

            RenderMode = RenderMode.GDI_PLUS;
            Core.CurrentRegion = new GMap.NET.Rectangle(-50, -50, Size.Width+100, Size.Height+100);

            CenterFormat.Alignment = StringAlignment.Center;
            CenterFormat.LineAlignment = StringAlignment.Center;

            // overlay testing
            GMapOverlay ov = new GMapOverlay(this, "base");
            Overlays.Add(ov);
         }
      }

      /// <summary>
      /// update objects when map is draged
      /// </summary>
      void GMap_OnMapDrag()
      {
         foreach(GMapOverlay o in Overlays)
         {
            if(o.IsVisibile)
            {
               foreach(GMapMarker obj in o.Markers)
               {
                  obj.Position = obj.Position;
               }

               foreach(GMapRoute obj in o.Routes)
               {
                  UpdateRouteLocalPosition(obj);
               }
            }
         }
      }

      /// <summary>
      /// thread safe invalidation
      /// </summary>
      internal void Core_OnNeedInvalidation()
      {
         if(this.InvokeRequired)
         {
            MethodInvoker m = delegate
            {
               Invalidate(false);
            };
            this.Invoke(m);
         }
         else
         {
            Invalidate(false);
         }
      }

      /// <summary>
      /// render map in GDI+
      /// </summary>
      /// <param name="g"></param>
      void DrawMapGDIplus(Graphics g)
      {
         for(int i = -Core.sizeOfMapArea.Width; i <= Core.sizeOfMapArea.Width; i++)
         {
            for(int j = -Core.sizeOfMapArea.Height; j <= Core.sizeOfMapArea.Height; j++)
            {
               Core.tilePoint = Core.centerTileXYLocation;
               Core.tilePoint.X += i;
               Core.tilePoint.Y += j;

               Tile t = Core.Matrix[Core.tilePoint];
               if(t != null)
               {
                  Core.tileRect.X = Core.tilePoint.X*Core.tileRect.Width;
                  Core.tileRect.Y = Core.tilePoint.Y*Core.tileRect.Height;
                  Core.tileRect.Offset(Core.renderOffset);

                  if(Core.CurrentRegion.IntersectsWith(Core.tileRect))
                  {
                     bool found = false;

                     // render tile
                     lock(t.Overlays)
                     {
                        foreach(WindowsFormsImage img in t.Overlays)
                        {
                           if(img != null && img.Img != null)
                           {
                              if(!found)
                                 found = true;

                              g.DrawImage(img.Img, Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height);
                           }
                        }
                     }

                     // add text if tile is missing
                     if(!found)
                     {
                        g.FillRectangle(EmptytileBrush, new RectangleF(Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height));
                        g.DrawString(EmptyTileText, MissingDataFont, Brushes.White, new RectangleF(Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height), CenterFormat);
                        g.DrawRectangle(EmptyTileBorders, Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height);

                        // raise error
                        if(OnEmptyTileError != null)
                        {
                           if(!RaiseEmptyTileError)
                           {
                              RaiseEmptyTileError = true;
                              OnEmptyTileError(t.Zoom, t.Pos);
                           }
                        }
                     }
                  }

                  //if(Core.tilePoint == Core.centerTileXYLocation)
                  //{
                  //   g.FillRectangle(EmptytileBrush, new RectangleF(Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height));
                  //   g.DrawString("CENTER TILE", MissingDataFont, Brushes.White, new RectangleF(Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height), CenterFormat);
                  //   g.DrawRectangle(EmptyTileBorders, Core.tileRect.X, Core.tileRect.Y, Core.tileRect.Width, Core.tileRect.Height);
                  //}
               }
            }
         }
      }

      /// <summary>
      /// updates route local position
      /// </summary>
      /// <param name="route"></param>
      public void UpdateRouteLocalPosition(GMapRoute route)
      {
         route.LocalPoints.Clear();

         foreach(GMap.NET.PointLatLng pg in route.Points)
         {
            GMap.NET.Point p = GMaps.Instance.FromLatLngToPixel(pg, Core.Zoom);
            p.Offset(Core.renderOffset);
            route.LocalPoints.Add(p);
         }
      }

      /// <summary>
      /// sets zoom to max to fit rect
      /// </summary>
      /// <param name="rect"></param>
      /// <returns></returns>
      public bool SetZoomToFitRect(RectLatLng rect)
      {
         int maxZoom = Core.GetMaxZoomToFitRect(rect);
         if(maxZoom > 0)
         {
            PointLatLng center = new PointLatLng(rect.Lat-(rect.HeightLat/2), rect.Lng+(rect.WidthLng/2));
            CurrentPosition = center;

            if(maxZoom > MaxZoom)
            {
               maxZoom = MaxZoom;
            }

            if(Zoom != maxZoom)
            {
               Zoom = maxZoom;
            }
            else
            {
               GoToCurrentPosition();
            }

            return true;
         }
         return false;
      }

      /// <summary>
      /// sets to max zoom to fit all markers and centers them in map
      /// </summary>
      /// <param name="overlayId">overlay id or null to check all</param>
      /// <returns></returns>
      public bool ZoomAndCenterMarkers(string overlayId)
      {
         RectLatLng? rect = GetRectOfAllMarkers(overlayId);
         if(rect.HasValue)
         {
            return SetZoomToFitRect(rect.Value);
         }

         return false;
      }

      /// <summary>
      /// zooms and centers all route
      /// </summary>
      /// <param name="overlayId">overlay id or null to check all</param>
      /// <returns></returns>
      public bool ZoomAndCenterRoutes(string overlayId)
      {
         RectLatLng? rect = GetRectOfAllRoutes(overlayId);
         if(rect.HasValue)
         {
            return SetZoomToFitRect(rect.Value);
         }

         return false;
      }

      /// <summary>
      /// zooms and centers route 
      /// </summary>
      /// <param name="route"></param>
      /// <returns></returns>
      public bool ZoomAndCenterRoute(MapRoute route)
      {
         RectLatLng? rect = GetRectOfRoute(route);
         if(rect.HasValue)
         {
            return SetZoomToFitRect(rect.Value);
         }

         return false;
      }

      /// <summary>
      /// gets rectangle with all objects inside
      /// </summary>
      /// <param name="overlayId">overlay id or null to check all</param>
      /// <returns></returns>
      public RectLatLng? GetRectOfAllMarkers(string overlayId)
      {
         RectLatLng? ret = null;

         double left = double.MaxValue;
         double top = double.MinValue;
         double right = double.MinValue;
         double bottom = double.MaxValue;

         foreach(GMapOverlay o in Overlays)
         {
            if(overlayId == null || o.Id == overlayId)
            {
               if(o.IsVisibile && o.Markers.Count > 0)
               {
                  foreach(GMapMarker m in o.Markers)
                  {
                     if(m.Visible)
                     {
                        // left
                        if(m.Position.Lng < left)
                        {
                           left = m.Position.Lng;
                        }

                        // top
                        if(m.Position.Lat > top)
                        {
                           top = m.Position.Lat;
                        }

                        // right
                        if(m.Position.Lng > right)
                        {
                           right = m.Position.Lng;
                        }

                        // bottom
                        if(m.Position.Lat < bottom)
                        {
                           bottom = m.Position.Lat;
                        }
                     }
                  }

                  ret = RectLatLng.FromLTRB(left, top, right, bottom);
               }
            }
         }

         return ret;
      }

      /// <summary>
      /// gets rectangle with all objects inside
      /// </summary>
      /// <param name="overlayId">overlay id or null to check all</param>
      /// <returns></returns>
      public RectLatLng? GetRectOfAllRoutes(string overlayId)
      {
         RectLatLng? ret = null;

         double left = double.MaxValue;
         double top = double.MinValue;
         double right = double.MinValue;
         double bottom = double.MaxValue;

         foreach(GMapOverlay o in Overlays)
         {
            if(overlayId == null || o.Id == overlayId)
            {
               if(o.IsVisibile && o.Routes.Count > 0)
               {
                  foreach(MapRoute route in o.Routes)
                  {
                     if(route.From.HasValue && route.To.HasValue)
                     {
                        foreach(PointLatLng p in route.Points)
                        {
                           // left
                           if(p.Lng < left)
                           {
                              left = p.Lng;
                           }

                           // top
                           if(p.Lat > top)
                           {
                              top = p.Lat;
                           }

                           // right
                           if(p.Lng > right)
                           {
                              right = p.Lng;
                           }

                           // bottom
                           if(p.Lat < bottom)
                           {
                              bottom = p.Lat;
                           }
                        }
                     }
                  }

                  ret = RectLatLng.FromLTRB(left, top, right, bottom);
               }
            }
         }

         return ret;
      }

      /// <summary>
      /// gets rect of route
      /// </summary>
      /// <param name="route"></param>
      /// <returns></returns>
      public RectLatLng? GetRectOfRoute(MapRoute route)
      {
         RectLatLng? ret = null;

         double left = double.MaxValue;
         double top = double.MinValue;
         double right = double.MinValue;
         double bottom = double.MaxValue;

         if(route.From.HasValue && route.To.HasValue)
         {
            foreach(PointLatLng p in route.Points)
            {
               // left
               if(p.Lng < left)
               {
                  left = p.Lng;
               }

               // top
               if(p.Lat > top)
               {
                  top = p.Lat;
               }

               // right
               if(p.Lng > right)
               {
                  right = p.Lng;
               }

               // bottom
               if(p.Lat < bottom)
               {
                  bottom = p.Lat;
               }
            }
            ret = RectLatLng.FromLTRB(left, top, right, bottom);
         }
         return ret;
      }

      #region UserControl Events

      protected bool DesignModeInConstruct
      {
         get
         {
            return (LicenseManager.UsageMode == LicenseUsageMode.Designtime);
         }
      }

      protected override void OnLoad(EventArgs e)
      {
         base.OnLoad(e);

         Core.StartSystem();
      }

      protected override void OnPaintBackground(PaintEventArgs e)
      {
         if(DesignMode)
         {
            e.Graphics.FillRectangle(Brushes.Gray, 0, 0, Width, Height);
         }
      }

      protected override void OnPaint(PaintEventArgs e)
      {
         // render map
         DrawMapGDIplus(e.Graphics);

         // render objects on each layer
         foreach(GMapOverlay o in Overlays)
         {
            if(o.IsVisibile)
            {
               o.Render(e.Graphics);
            }
         }

         #region -- copyright --

         switch(Core.MapType)
         {
            case MapType.GoogleMap:
            case MapType.GoogleSatellite:
            case MapType.GoogleLabels:
            case MapType.GoogleTerrain:
            case MapType.GoogleHybrid:
            {
               e.Graphics.DrawString(Core.googleCopyright, CopyrightFont, Brushes.Navy, 3, Height - CopyrightFont.Height - 5);
            }
            break;

            case MapType.OpenStreetMap:
            case MapType.OpenStreetOsm:
            {
               e.Graphics.DrawString(Core.openStreetMapCopyright, CopyrightFont, Brushes.Navy, 3, Height - CopyrightFont.Height - 5);
            }
            break;

            case MapType.YahooMap:
            case MapType.YahooSatellite:
            case MapType.YahooLabels:
            case MapType.YahooHybrid:
            {
               e.Graphics.DrawString(Core.yahooMapCopyright, CopyrightFont, Brushes.Navy, 3, Height - CopyrightFont.Height - 5);
            }
            break;

            case MapType.VirtualEarthHybrid:
            case MapType.VirtualEarthMap:
            case MapType.VirtualEarthSatellite:
            {
               e.Graphics.DrawString(Core.virtualEarthCopyright, CopyrightFont, Brushes.Navy, 3, Height - CopyrightFont.Height - 5);
            }
            break;
         }

         #endregion
      }

      protected override void OnResize(EventArgs e)
      {
         base.OnResize(e);

         if(this.Visible)
         {
            this.Invalidate(false);
         }
      }

      protected override void OnSizeChanged(EventArgs e)
      {
         Core.sizeOfMapArea.Width = 1 + (Width/GMaps.Instance.TileSize.Width)/2;
         Core.sizeOfMapArea.Height = 1 + (Height/GMaps.Instance.TileSize.Height)/2;

         // 50px outside control
         Core.CurrentRegion = new GMap.NET.Rectangle(-50, -50, Size.Width+100, Size.Height+100);

         Core.OnMapSizeChanged(Width, Height);

         Core.GoToCurrentPosition();

         base.OnSizeChanged(e);
      }

      protected override void OnMouseDown(MouseEventArgs e)
      {
         Core.mouseDown.X = e.X;
         Core.mouseDown.Y = e.Y;

         if(e.Button == MouseButtons.Left)
         {
            if(!IsMouseOverMarker)
            {
               SetCurrentPositionOnly(e.X - Core.renderOffset.X, e.Y - Core.renderOffset.Y);

               if(Core.MouseVisible)
               {
                  this.Cursor = System.Windows.Forms.Cursors.Default;
                  Cursor.Hide();
                  Core.MouseVisible = false;
               }

               Core.BeginDrag(Core.mouseDown);
            }
         }
         else if(e.Button == MouseButtons.Right)
         {
            if(CanDragMap)
            {
               this.Cursor = System.Windows.Forms.Cursors.SizeAll;

               Core.BeginDrag(Core.mouseDown);
            }
         }

         this.Invalidate(false);

         base.OnMouseDown(e);
      }

      protected override void OnMouseUp(MouseEventArgs e)
      {
         if(Core.IsDragging)
         {
            Core.EndDrag();
         }

         this.Cursor = System.Windows.Forms.Cursors.Default;

         if(!Core.MouseVisible)
         {
            Cursor.Show();
            Core.MouseVisible = true;
         }

         RaiseEmptyTileError = false;

         base.OnMouseUp(e);
      }

      protected override void OnMouseClick(MouseEventArgs e)
      {
         if(e.Button == MouseButtons.Left && !Core.IsDragging)
         {
            for(int i = Overlays.Count-1; i >= 0; i--)
            {
               GMapOverlay o = Overlays[i];
               if(o != null && o.IsVisibile)
               {
                  foreach(GMapMarker m in o.Markers)
                  {
                     if(m.Visible)
                     {
                        if(m.LocalArea.Contains(e.X, e.Y))
                        {
                           if(OnMarkerClick != null)
                           {
                              OnMarkerClick(m);
                              break;
                           }
                        }
                     }
                  }
               }
            }
         }

         base.OnMouseClick(e);
      }

      protected override void OnMouseWheel(MouseEventArgs e)
      {
         base.OnMouseWheel(e);

         if(!IsMouseOverMarker && !IsDragging)
         {
            if(CenterPositionOnMouseWheel)
            {
               PointLatLng pg = FromLocalToLatLng(e.X, e.Y);
               SetCurrentPositionOnly(pg);

               System.Drawing.Point p = PointToScreen(new System.Drawing.Point(Width/2, Height/2));
               Stuff.SetCursorPos((int) p.X, (int) p.Y);
            }

            if(e.Delta > 0)
            {
               Zoom++;
            }
            else if(e.Delta < 0)
            {
               Zoom--;
            }
         }
      }

      protected override void OnMouseMove(MouseEventArgs e)
      {
         if(Core.IsDragging)
         {
            Core.mouseCurrent.X = e.X;
            Core.mouseCurrent.Y = e.Y;

            if(e.Button == MouseButtons.Right)
            {
               Core.Drag(Core.mouseCurrent);
            }
            else if(e.Button == MouseButtons.Left)
            {
               SetCurrentPositionOnly(e.X - Core.renderOffset.X, e.Y - Core.renderOffset.Y);
               Invalidate(false);
            }
         }
         else
         {
            for(int i = Overlays.Count-1; i >= 0; i--)
            {
               GMapOverlay o = Overlays[i];
               if(o != null && o.IsVisibile)
               {
                  foreach(GMapMarker m in o.Markers)
                  {
                     if(m.Visible)
                     {
                        if(m.LocalArea.Contains(e.X, e.Y))
                        {
                           this.Cursor = System.Windows.Forms.Cursors.Hand;
                           m.IsMouseOver = true;
                           IsMouseOverMarker = true;
                           Invalidate(false);

                           if(OnMarkerEnter != null)
                           {
                              OnMarkerEnter(m);
                           }
                        }
                        else if(m.IsMouseOver)
                        {
                           this.Cursor = System.Windows.Forms.Cursors.Default;
                           m.IsMouseOver = false;
                           IsMouseOverMarker = false;
                           Invalidate(false);

                           if(OnMarkerLeave != null)
                           {
                              OnMarkerLeave(m);
                           }
                        }
                     }
                  }
               }
            }
         }
         base.OnMouseMove(e);
      }

      #endregion

      #region IGControl Members

      /// <summary>
      /// reloads the map
      /// </summary>
      public void ReloadMap()
      {
         Core.ReloadMap();
      }

      /// <summary>
      /// sets current position into the center
      /// </summary>
      public void GoToCurrentPosition()
      {
         Core.GoToCurrentPosition();
      }

      /// <summary>
      /// set current position using keywords
      /// </summary>
      /// <param name="keys"></param>
      /// <returns>true if successfull</returns>
      public bool SetCurrentPositionByKeywords(string keys)
      {
         return Core.SetCurrentPositionByKeywords(keys);
      }

      /// <summary>
      /// gets world coordinate from local control coordinate 
      /// </summary>
      /// <param name="x"></param>
      /// <param name="y"></param>
      /// <returns></returns>
      public PointLatLng FromLocalToLatLng(int x, int y)
      {
         return Core.FromLocalToLatLng(x, y);
      }

      /// <summary>
      /// gets local coordinate from world coordinate
      /// </summary>
      /// <param name="point"></param>
      /// <returns></returns>
      public GMap.NET.Point FromLatLngToLocal(PointLatLng point)
      {
         return Core.FromLatLngToLocal(point);
      }

      /// <summary>
      /// changes current position without changing current gtile
      /// using pixel coordinates
      /// </summary>
      /// <param name="x"></param>
      /// <param name="y"></param>
      public void SetCurrentPositionOnly(int x, int y)
      {
         Core.SetCurrentPositionOnly(x, y);
      }

      /// <summary>
      /// changes current position without changing current gtile
      /// </summary>
      /// <param name="point"></param>
      public void SetCurrentPositionOnly(PointLatLng point)
      {
         Core.SetCurrentPositionOnly(point);
      }

      /// <summary>
      /// shows map db export dialog
      /// </summary>
      /// <returns></returns>
      public bool ShowExportDialog()
      {
         using(FileDialog dlg = new SaveFileDialog())
         {
            dlg.CheckPathExists = true;
            dlg.CheckFileExists = false;
            dlg.AddExtension = true;
            dlg.DefaultExt = "gmdb";
            dlg.ValidateNames = true;
            dlg.Title = "GMap.NET: Export map to db, if file exsist only new data will be added";
            dlg.FileName = "DataExp";
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            dlg.Filter = "GMap.NET DB files (*.gmdb)|*.gmdb";
            dlg.FilterIndex = 1;
            dlg.RestoreDirectory = true;

            if(dlg.ShowDialog() == DialogResult.OK)
            {
               bool ok = GMaps.Instance.ExportToGMDB(dlg.FileName);
               if(ok)
               {
                  MessageBox.Show("Complete!", "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Information);
               }
               else
               {
                  MessageBox.Show("  Failed!", "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
               }

               return ok;
            }
         }
         return false;
      }

      /// <summary>
      /// shows map dbimport dialog
      /// </summary>
      /// <returns></returns>
      public bool ShowImportDialog()
      {
         using(FileDialog dlg = new OpenFileDialog())
         {
            dlg.CheckPathExists = true;
            dlg.CheckFileExists = false;
            dlg.AddExtension = true;
            dlg.DefaultExt = "gmdb";
            dlg.ValidateNames = true;
            dlg.Title = "GMap.NET: Import to db, only new data will be added";
            dlg.FileName = "DataExp";
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            dlg.Filter = "GMap.NET DB files (*.gmdb)|*.gmdb";
            dlg.FilterIndex = 1;
            dlg.RestoreDirectory = true;

            if(dlg.ShowDialog() == DialogResult.OK)
            {
               bool ok = GMaps.Instance.ImportFromGMDB(dlg.FileName);
               if(ok)
               {
                  MessageBox.Show("Complete!", "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Information);
               }
               else
               {
                  MessageBox.Show("  Failed!", "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
               }

               return ok;
            }
         }
         return false;
      }

      /// <summary>
      /// map zoom level
      /// </summary>
      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
      public int Zoom
      {
         get
         {
            return Core.Zoom;
         }
         set
         {
            if(value <= MaxZoom && value >= MinZoom)
            {
               Core.Zoom = value;
            }
         }
      }

      /// <summary>
      /// current marker position
      /// </summary>
      public PointLatLng CurrentPosition
      {
         get
         {
            return Core.CurrentPosition;
         }
         set
         {
            Core.CurrentPosition = value;
         }
      }

      /// <summary>
      /// current marker position in pixel coordinates
      /// </summary>
      public GMap.NET.Point CurrentPositionGPixel
      {
         get
         {
            return Core.CurrentPositionGPixel;
         }
      }

      /// <summary>
      /// google tile in which current marker is
      /// </summary>
      public GMap.NET.Point CurrentPositionGTile
      {
         get
         {
            return Core.CurrentPositionGTile;
         }
      }

      /// <summary>
      /// location of cache
      /// </summary>
      [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
      public string CacheLocation
      {
         get
         {
            return Cache.Instance.CacheLocation;
         }
         set
         {
            Cache.Instance.CacheLocation = value;
         }
      }

      /// <summary>
      /// total count of google tiles at current zoom
      /// </summary>
      public long TotalTiles
      {
         get
         {
            return Core.TotalTiles;
         }
      }

      /// <summary>
      /// is user dragging map
      /// </summary>
      public bool IsDragging
      {
         get
         {
            return Core.IsDragging;
         }
      }

      /// <summary>
      /// is mouse over marker
      /// </summary>
      public bool IsMouseOverMarker;

      /// <summary>
      /// gets current map view top/left coordinate, width in Lng, height in Lat
      /// </summary>
      public RectLatLng CurrentViewArea
      {
         get
         {
            return Core.CurrentViewArea;
         }
      }

      /// <summary>
      /// for tooltip text padding
      /// </summary>
      public GMap.NET.Size TooltipTextPadding
      {
         get
         {
            return Core.TooltipTextPadding;
         }
         set
         {
            Core.TooltipTextPadding = value;
         }
      }

      /// <summary>
      /// type of map
      /// </summary>
      public MapType MapType
      {
         get
         {
            return Core.MapType;
         }
         set
         {
            Core.MapType = value;
         }
      }

      /// <summary>
      /// is routes enabled
      /// </summary>
      public bool RoutesEnabled
      {
         get
         {
            return Core.RoutesEnabled;
         }
         set
         {
            Core.RoutesEnabled = value;
         }
      }

      /// <summary>
      /// is markers enabled
      /// </summary>
      public bool MarkersEnabled
      {
         get
         {
            return Core.MarkersEnabled;
         }
         set
         {
            Core.MarkersEnabled = value;
         }
      }

      /// <summary>
      /// can user drag map
      /// </summary>
      public bool CanDragMap
      {
         get
         {
            return Core.CanDragMap;
         }
         set
         {
            Core.CanDragMap = value;
         }
      }

      /// <summary>
      /// map render mode
      /// </summary>
      public RenderMode RenderMode
      {
         get
         {
            return Core.RenderMode;
         }
         internal set
         {
            Core.RenderMode = value;
         }
      }

      #endregion

      #region IGControl event Members

      /// <summary>
      /// occurs when current position is changed
      /// </summary>
      public event CurrentPositionChanged OnCurrentPositionChanged
      {
         add
         {
            Core.OnCurrentPositionChanged += value;
         }
         remove
         {
            Core.OnCurrentPositionChanged -= value;
         }
      }

      /// <summary>
      /// occurs when tile set load is complete
      /// </summary>
      public event TileLoadComplete OnTileLoadComplete
      {
         add
         {
            Core.OnTileLoadComplete += value;
         }
         remove
         {
            Core.OnTileLoadComplete -= value;
         }
      }

      /// <summary>
      /// occurs when tile set is starting to load
      /// </summary>
      public event TileLoadStart OnTileLoadStart
      {
         add
         {
            Core.OnTileLoadStart += value;
         }
         remove
         {
            Core.OnTileLoadStart -= value;
         }
      }

      /// <summary>
      /// occurs on map drag
      /// </summary>
      public event MapDrag OnMapDrag
      {
         add
         {
            Core.OnMapDrag += value;
         }
         remove
         {
            Core.OnMapDrag -= value;
         }
      }

      /// <summary>
      /// occurs on map zoom changed
      /// </summary>
      public event MapZoomChanged OnMapZoomChanged
      {
         add
         {
            Core.OnMapZoomChanged += value;
         }
         remove
         {
            Core.OnMapZoomChanged -= value;
         }
      }

      #endregion
   }

   #region -- purity control --
   internal class WindowsFormsImage : PureImage
   {
      public System.Drawing.Image Img;

      public override object Clone()
      {
         if(Img != null)
         {
            WindowsFormsImage ret = new WindowsFormsImage();
            ret.Img = Img.Clone() as Image;
            return ret;
         }
         return null;
      }

      public override void Dispose()
      {
         if(Img != null)
         {
            Img.Dispose();
            Img = null;
         }
      }
   }

   internal class WindowsFormsImageProxy : PureImageProxy
   {
      public override PureImage FromStream(Stream stream)
      {
         WindowsFormsImage ret = null;
         try
         {
            Image m = Image.FromStream(stream, true, true);
            if(m != null)
            {
               ret = new WindowsFormsImage();
               ret.Img = m;
            }
         }
         catch
         {
            ret = null;
         }
         return ret;
      }

      public override bool Save(Stream stream, GMap.NET.PureImage image)
      {
         WindowsFormsImage ret = image as WindowsFormsImage;
         bool ok = true;

         if(ret.Img != null)
         {
            // try png
            try
            {
               ret.Img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch
            {
               // try jpeg
               try
               {
                  stream.Seek(0, SeekOrigin.Begin);
                  ret.Img.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
               }
               catch
               {
                  ok = false;
               }
            }             
         }
         else
         {
            ok = false;
         }

         return ok;
      }
   }
   #endregion
}