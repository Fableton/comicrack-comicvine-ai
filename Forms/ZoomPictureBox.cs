using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>
	/// Shows an image fitted to the control. The mouse wheel zooms at the pointer, dragging moves the image and a
	/// double click fits it again. Owns the image it shows.
	/// </summary>
	public sealed class ZoomPictureBox : Control
	{
		private const float MinZoom = 0.05f;
		private const float MaxZoom = 12f;

		private Bitmap image;
		private bool fit = true;
		private float zoom = 1f;
		private PointF offset; // position of the top left corner of the image inside the control
		private Point dragStart;
		private PointF dragOffset;
		private bool dragging;

		public ZoomPictureBox()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			BackColor = Color.FromArgb(240, 240, 240);
			TabStop = true;
		}

		public Bitmap Image
		{
			get => image;
			set
			{
				Bitmap old = image;
				image = value;
				old?.Dispose();
				FitToWindow();
			}
		}

		/// <summary>The current zoom, 1.0 = the real size of the image.</summary>
		public float ZoomFactor => image == null ? 1f : (fit ? FitScale() : zoom);

		private float FitScale() => image == null ? 1f : Math.Min((float)ClientSize.Width / image.Width, (float)ClientSize.Height / image.Height);

		public void FitToWindow()
		{
			fit = true;
			Center();
			Invalidate();
		}

		public void ActualSize()
		{
			ZoomTo(1f, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
		}

		public void ZoomBy(float factor) => ZoomBy(factor, new Point(ClientSize.Width / 2, ClientSize.Height / 2));

		public void ZoomBy(float factor, Point around) => ZoomTo(ZoomFactor * factor, around);

		private void ZoomTo(float newZoom, Point around)
		{
			if (image == null)
			{
				return;
			}
			float old = ZoomFactor;
			newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
			// keep the point under the pointer where it is
			float imageX = (around.X - offset.X) / old;
			float imageY = (around.Y - offset.Y) / old;
			zoom = newZoom;
			fit = false;
			offset = new PointF(around.X - imageX * zoom, around.Y - imageY * zoom);
			Clamp();
			Invalidate();
		}

		private void Center()
		{
			if (image == null)
			{
				return;
			}
			float scale = ZoomFactor;
			offset = new PointF((ClientSize.Width - image.Width * scale) / 2f, (ClientSize.Height - image.Height * scale) / 2f);
		}

		/// <summary>A smaller image is centered; a larger one cannot be moved out of the view.</summary>
		private void Clamp()
		{
			if (image == null)
			{
				return;
			}
			float w = image.Width * ZoomFactor;
			float h = image.Height * ZoomFactor;
			float x = w <= ClientSize.Width ? (ClientSize.Width - w) / 2f : Math.Min(0, Math.Max(ClientSize.Width - w, offset.X));
			float y = h <= ClientSize.Height ? (ClientSize.Height - h) / 2f : Math.Min(0, Math.Max(ClientSize.Height - h, offset.Y));
			offset = new PointF(x, y);
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (fit)
			{
				Center();
			}
			else
			{
				Clamp();
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.Clear(BackColor);
			if (image != null)
			{
				float scale = ZoomFactor;
				e.Graphics.InterpolationMode = scale < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bicubic;
				e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
				e.Graphics.DrawImage(image, new RectangleF(offset.X, offset.Y, image.Width * scale, image.Height * scale));
			}
			using (Pen border = new Pen(Color.Gray))
			{
				e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
			}
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			ZoomBy(e.Delta > 0 ? 1.18f : 1f / 1.18f, e.Location);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
			if (e.Button == MouseButtons.Left && image != null)
			{
				dragging = true;
				dragStart = e.Location;
				dragOffset = offset;
				Cursor = Cursors.Hand;
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (dragging && !fit) // fitted, the whole image is visible: nothing to move
			{
				offset = new PointF(dragOffset.X + e.X - dragStart.X, dragOffset.Y + e.Y - dragStart.Y);
				Clamp();
				Invalidate();
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			dragging = false;
			Cursor = Cursors.Default;
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			FitToWindow();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				image?.Dispose();
				image = null;
			}
			base.Dispose(disposing);
		}
	}
}
