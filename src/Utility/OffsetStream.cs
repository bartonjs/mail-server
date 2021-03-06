using System.IO;

namespace Vaettir.Utility
{
	public class OffsetStream : Stream
	{
		private readonly Stream _baseStream;
		private readonly long _offset;

		public OffsetStream(Stream baseStream, long offset)
		{
			_baseStream = baseStream;
			_offset = offset;
		}

		public OffsetStream(Stream inProgressStream)
		{
			_baseStream = inProgressStream;
			_offset = inProgressStream.Position;
		}

		public override void Flush()
		{
			_baseStream.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return _baseStream.Read(buffer, offset, count);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			if (origin == SeekOrigin.Begin)
			{
				return _baseStream.Seek(offset - _offset, origin);
			}
			else
			{
				return _baseStream.Seek(offset, origin);
			}
		}

		public override void SetLength(long value)
		{
			_baseStream.SetLength(value + _offset);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			_baseStream.Write(buffer, offset, count);
		}

		public override bool CanRead => _baseStream.CanRead;

		public override bool CanSeek => _baseStream.CanSeek;

		public override bool CanWrite => _baseStream.CanWrite;

		public override long Length => _baseStream.Length - _offset;

		public override long Position
		{
			get { return _baseStream.Position - _offset; }
			set { _baseStream.Position = value + _offset; }
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_baseStream.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}