﻿#region -- License Terms --
//
// MessagePack for CLI
//
// Copyright (C) 2010-2012 FUJIWARA, Yusuke
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
#endregion -- License Terms --

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MsgPack
{
	// TODO: Code comment for protected specifically <exception>
	// TODO: Refactoring or use T4 template.

	/*
	 *  Convention
	 *  PackXxx (public, provides overloads) -> PackXxxCore(protected virtual, provides validation) 
	 *  -> PrivatePackXxx(private, provides null handling) -> PrivatePackXxxCore(private, provides serialization)
	 * 
	 */

	/// <summary>
	///		Implements serialization feature of MsgPack.
	/// </summary>
	public abstract partial class Packer : IDisposable
	{
		private bool _isDisposed;

		/// <summary>
		///		Get whether this class supports seek operation and quering <see cref="Position"/> property.
		/// </summary>
		/// <value>If this class supports seek operation and quering <see cref="Position"/> property then true.</value>
		public virtual bool CanSeek
		{
			get { return false; }
		}

		/// <summary>
		///		Get current position of underlying stream.
		/// </summary>
		/// <value>Opaque position value of underlying stream.</value>
		/// <exception cref="NotSupportedException">
		///		A class of this instance does not support seek.
		/// </exception>
		public virtual long Position
		{
			get { throw new NotSupportedException(); }
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Packer"/> class.
		/// </summary>
		protected Packer() { }

		/// <summary>
		///		Create standard Safe <see cref="Packer"/> instancde wrapping specified <see cref="Stream"/>.
		/// </summary>
		/// <param name="stream"><see cref="Stream"/> object. This stream will be closed when <see cref="Packer.Dispose(Boolean)"/> is called.</param>
		/// <returns>Safe <see cref="Packer"/>. This will not be null.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
		/// <remarks>
		///		 You can specify any derived <see cref="Stream"/> class like <see cref="FileStream"/>, <see cref="MemoryStream"/>,
		///		 NetworkStream, <see cref="UnmanagedMemoryStream"/>, or so.
		/// </remarks>
		public static Packer Create( Stream stream )
		{
			return Create( stream, true );
		}

		/// <summary>
		///		Create standard Safe <see cref="Packer"/> instancde wrapping specified <see cref="Stream"/>.
		/// </summary>
		/// <param name="stream"><see cref="Stream"/> object.</param>
		/// <param name="ownsStream">
		///		<c>true</c> to close <paramref name="stream"/> when this instance is disposed;
		///		<c>false</c>, otherwise.
		/// </param>
		/// <returns>Safe <see cref="Packer"/>. This will not be null.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
		/// <remarks>
		///		 You can specify any derived <see cref="Stream"/> class like <see cref="FileStream"/>, <see cref="MemoryStream"/>,
		///		 NetworkStream, <see cref="UnmanagedMemoryStream"/>, or so.
		/// </remarks>
		public static Packer Create( Stream stream, bool ownsStream )
		{
			if ( stream == null )
			{
				throw new ArgumentNullException( "stream" );
			}

			return new StreamPacker( stream, ownsStream );
		}

		/// <summary>
		///		Clean up internal resources.
		/// </summary>
		public void Dispose()
		{
			if ( this._isDisposed )
			{
				return;
			}

			this.Dispose( true );
			GC.SuppressFinalize( this );
			this._isDisposed = true;
		}

		/// <summary>
		///		When overridden by derived class, release all unmanaged resources, optionally release managed resources.
		/// </summary>
		/// <param name="disposing">If true, release managed resources too.</param>
		protected virtual void Dispose( bool disposing ) { }

		private void VerifyNotDisposed()
		{
			if ( this._isDisposed )
			{
				throw new ObjectDisposedException( this.ToString() );
			}
		}

		/// <summary>
		///		When overridden by derived class, change current position to specified offset.
		/// </summary>
		/// <param name="offset">Offset. You shoud not specify the value which causes underflow or overflow.</param>
		/// <exception cref="NotSupportedException">
		///		A class of this instance does not support seek.
		/// </exception>
		protected virtual void SeekTo( long offset )
		{
			throw new NotSupportedException();
		}

		/// <summary>
		///		When overridden by derived class, writes specified byte to stream using implementation specific manner.
		/// </summary>
		/// <param name="value">A byte to be written.</param>
		protected abstract void WriteByte( byte value );

		#region -- Bulk writing --

		/// <summary>
		///		Writes specified bytes to stream using implementation specific most efficient manner.
		/// </summary>
		/// <param name="value">Collection of bytes to be written.</param>
		protected virtual void WriteBytes( ICollection<byte> value )
		{
			if ( value == null )
			{
				throw new ArgumentNullException( "value" );
			}

			Contract.EndContractBlock();

			foreach ( var b in value )
			{
				this.WriteByte( b );
			}
		}

		/// <summary>
		///		Writes specified bytes to stream using implementation specific most efficient manner.
		/// </summary>
		/// <param name="value">Bytes to be written.</param>
		/// <param name="isImmutable">If the <paramref name="value"/> can be treat as immutable (that is, can be used safely without copying) then <c>true</c>.</param>
		protected virtual void WriteBytes( byte[] value, bool isImmutable )
		{
			if ( value == null )
			{
				throw new ArgumentNullException( "value" );
			}

			Contract.EndContractBlock();

			foreach ( var b in value )
			{
				this.WriteByte( b );
			}
		}

		private void StreamWrite<TItem>( IEnumerable<TItem> value, Action<IEnumerable<TItem>, PackingOptions> writeBody, PackingOptions options )
		{
			if ( this.CanSeek )
			{
				// Reserve length
				this.SeekTo( 4L );
				var headerPosition = this.Position;
				// Write body
				writeBody( value, options );
				var bodyLength = this.Position - headerPosition;
				// Back to reserved length
				this.SeekTo( -bodyLength );
				this.SeekTo( -4L );
				unchecked
				{
					this.WriteByte( ( byte )( ( bodyLength >> 24 ) & 0xff ) );
					this.WriteByte( ( byte )( ( bodyLength >> 16 ) & 0xff ) );
					this.WriteByte( ( byte )( ( bodyLength >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( bodyLength & 0xff ) );
				}
				// Forward to body tail
				this.SeekTo( bodyLength );
			}
			else
			{
				// Copying is better than forcing stream is seekable...
				ICollection<TItem> asCollection = value as ICollection<TItem>;
				if ( asCollection == null )
				{
					asCollection = value.ToArray();
				}

				var bodyLength = asCollection.Count;
				unchecked
				{
					this.WriteByte( ( byte )( ( bodyLength >> 24 ) & 0xff ) );
					this.WriteByte( ( byte )( ( bodyLength >> 16 ) & 0xff ) );
					this.WriteByte( ( byte )( ( bodyLength >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( bodyLength & 0xff ) );
				}

				writeBody( asCollection, options );
			}
		}

		#endregion -- Bulk writing --

		#region -- Int8 --

		/// <summary>
		///		Pack <see cref="SByte"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="SByte"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		[CLSCompliant( false )]
		public Packer Pack( sbyte value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( sbyte value )
		{
			if ( this.TryPackTinySignedInteger( value ) )
			{
				return;
			}

			var b = this.TryPackInt8( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="SByte"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="SByte"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		protected bool TryPackInt8( long value )
		{
			if ( value > SByte.MaxValue || value < SByte.MinValue )
			{
				return false;
			}

			this.WriteByte( MessagePackCode.SignedInt8 );
			this.WriteByte( ( byte )value );
			return true;
		}

		#endregion -- Int8 --

		#region -- UInt8 --

		/// <summary>
		///		Pack <see cref="Byte"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Byte"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer Pack( byte value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( byte value )
		{
			if ( this.TryPackTinyUnsignedInteger( value ) )
			{
				return;
			}

			var b = this.TryPackUInt8( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="Byte"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="Byte"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		private bool TryPackUInt8( ulong value )
		{
			if ( value > Byte.MaxValue )
			{
				return false;
			}

			this.WriteByte( MessagePackCode.UnsignedInt8 );
			this.WriteByte( ( byte )value );
			return true;
		}

		#endregion -- UInt8 --

		#region -- Int16 --

		/// <summary>
		///		Pack <see cref="Int16"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Int16"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer Pack( short value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( short value )
		{
			if ( this.TryPackTinySignedInteger( value ) )
			{
				return;
			}

			if ( this.TryPackInt8( value ) )
			{
				return;
			}

			var b = this.TryPackInt16( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="Int16"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="Int16"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		protected bool TryPackInt16( long value )
		{
			if ( value < Int16.MinValue || value > Int16.MaxValue )
			{
				return false;
			}

			this.WriteByte( MessagePackCode.SignedInt16 );
			unchecked
			{
				this.WriteByte( ( byte )( ( value >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( value & 0xff ) );
			}
			return true;
		}

		#endregion -- Int16 --

		#region -- UInt16 --

		/// <summary>
		///		Pack <see cref="UInt16"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="UInt16"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		[CLSCompliant( false )]
		public Packer Pack( ushort value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( ushort value )
		{
			if ( this.TryPackTinyUnsignedInteger( value ) )
			{
				return;
			}

			if ( this.TryPackUInt8( value ) )
			{
				return;
			}

			var b = this.TryPackUInt16( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="UInt16"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="UInt16"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		[CLSCompliant( false )]
		protected bool TryPackUInt16( ulong value )
		{
			if ( value > UInt16.MaxValue )
			{
				return false;
			}

			this.WriteByte( MessagePackCode.UnsignedInt16 );
			unchecked
			{
				this.WriteByte( ( byte )( ( value >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( value & 0xff ) );
			}
			return true;
		}

		#endregion -- UInt16 --

		#region -- Int32 --

		/// <summary>
		///		Pack <see cref="Int32"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Int32"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer Pack( int value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( int value )
		{
			if ( this.TryPackTinySignedInteger( value ) )
			{
				return;
			}

			if ( this.TryPackInt8( value ) )
			{
				return;
			}

			if ( this.TryPackInt16( value ) )
			{
				return;
			}

			var b = this.TryPackInt32( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="Int32"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="Int32"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		protected bool TryPackInt32( long value )
		{
			if ( value > Int32.MaxValue || value < Int32.MinValue )
			{
				return false;
			}

			this.WriteByte( MessagePackCode.SignedInt32 );
			unchecked
			{
				this.WriteByte( ( byte )( ( value >> 24 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 16 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( value & 0xff ) );
			}
			return true;
		}

		#endregion -- Int32 --

		#region -- UInt32 --

		/// <summary>
		///		Pack <see cref="UInt32"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="UInt32"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		[CLSCompliant( false )]
		public Packer Pack( uint value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( uint value )
		{

			if ( this.TryPackTinyUnsignedInteger( value ) )
			{
				return;
			}

			if ( this.TryPackUInt8( value ) )
			{
				return;
			}

			if ( this.TryPackUInt16( value ) )
			{
				return;
			}

			var b = this.TryPackUInt32( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="UInt32"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="UInt32"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		[CLSCompliant( false )]
		protected bool TryPackUInt32( ulong value )
		{
			if ( value > UInt32.MaxValue )
			{
				return false;
			}

			this.WriteByte( MessagePackCode.UnsignedInt32 );
			unchecked
			{
				this.WriteByte( ( byte )( ( value >> 24 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 16 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( value & 0xff ) );
			}
			return true;
		}

		#endregion -- UInt32 --

		#region -- Int64 --

		/// <summary>
		///		Pack <see cref="Int64"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Int64"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer Pack( long value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( long value )
		{
			if ( this.TryPackTinySignedInteger( value ) )
			{
				return;
			}

			if ( this.TryPackInt8( value ) )
			{
				return;
			}

			if ( this.TryPackInt16( value ) )
			{
				return;
			}

			if ( this.TryPackInt32( value ) )
			{
				return;
			}

			var b = this.TryPackInt64( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="Int64"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="Int64"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		protected bool TryPackInt64( long value )
		{
			this.WriteByte( MessagePackCode.SignedInt64 );
			unchecked
			{
				this.WriteByte( ( byte )( ( value >> 56 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 48 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 40 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 32 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 24 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 16 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( value & 0xff ) );
			}
			return true;
		}

		#endregion -- Int64 --

		#region -- UInt64 --

		/// <summary>
		///		Pack <see cref="UInt64"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="UInt64"/> value.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		[CLSCompliant( false )]
		public Packer Pack( ulong value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( ulong value )
		{
			if ( this.TryPackTinyUnsignedInteger( value ) )
			{
				return;
			}

			if ( this.TryPackUInt8( value ) )
			{
				return;
			}

			if ( this.TryPackUInt16( value ) )
			{
				return;
			}

			if ( this.TryPackUInt32( value ) )
			{
				return;
			}

			var b = this.TryPackUInt64( value );
			Contract.Assume( b );
		}

		/// <summary>
		///		Try pack <see cref="UInt64"/> value to current stream strictly.
		/// </summary>
		/// <param name="value">Maybe <see cref="UInt64"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		[CLSCompliant( false )]
		protected bool TryPackUInt64( ulong value )
		{
			this.WriteByte( MessagePackCode.UnsignedInt64 );
			unchecked
			{
				this.WriteByte( ( byte )( ( value >> 56 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 48 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 40 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 32 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 24 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 16 ) & 0xff ) );
				this.WriteByte( ( byte )( ( value >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( value & 0xff ) );
			}
			return true;
		}

		#endregion -- UInt64 --

		#region -- Single --

		/// <summary>
		///		Pack <see cref="Single"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Single"/> value.</param>
		/// <returns>This instance.</returns>
		public Packer Pack( float value )
		{
			this.VerifyNotDisposed();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( float value )
		{
			this.WriteByte( MessagePackCode.Real32 );

			var bits = new Float32Bits( value );

			if ( BitConverter.IsLittleEndian )
			{
				this.WriteByte( bits.Byte3 );
				this.WriteByte( bits.Byte2 );
				this.WriteByte( bits.Byte1 );
				this.WriteByte( bits.Byte0 );
			}
			else
			{
				this.WriteByte( bits.Byte0 );
				this.WriteByte( bits.Byte1 );
				this.WriteByte( bits.Byte2 );
				this.WriteByte( bits.Byte3 );
			}
		}

		#endregion -- Single --

		#region -- Double --

		/// <summary>
		///		Pack <see cref="Double"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Double"/> value.</param>
		/// <returns>This instance.</returns>
		public Packer Pack( double value )
		{
			this.VerifyNotDisposed();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( double value )
		{
			this.WriteByte( MessagePackCode.Real64 );
			unchecked
			{
				long bits = BitConverter.DoubleToInt64Bits( value );
				this.WriteByte( ( byte )( ( bits >> 56 ) & 0xff ) );
				this.WriteByte( ( byte )( ( bits >> 48 ) & 0xff ) );
				this.WriteByte( ( byte )( ( bits >> 40 ) & 0xff ) );
				this.WriteByte( ( byte )( ( bits >> 32 ) & 0xff ) );
				this.WriteByte( ( byte )( ( bits >> 24 ) & 0xff ) );
				this.WriteByte( ( byte )( ( bits >> 16 ) & 0xff ) );
				this.WriteByte( ( byte )( ( bits >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( bits & 0xff ) );
			}
		}

		#endregion -- Double --

		#region -- Boolean --

		/// <summary>
		///		Pack <see cref="Boolean"/> value to current stream.
		/// </summary>
		/// <param name="value"><see cref="Boolean"/> value.</param>
		/// <returns>This instance.</returns>
		public Packer Pack( bool value )
		{
			this.VerifyNotDisposed();
			this.PrivatePackCore( value );
			return this;
		}

		private void PrivatePackCore( bool value )
		{
			this.WriteByte( value ? ( byte )MessagePackCode.TrueValue : ( byte )MessagePackCode.FalseValue );
		}

		#endregion -- Boolean --

		#region -- Collection Header --

		/// <summary>
		///		Bookkeep array length to be packed on current stream.
		/// </summary>
		/// <param name="count">Array length.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackArrayHeader( int count )
		{
			this.PackArrayHeaderCore( count );
			return this;
		}

		/// <summary>
		///		Bookkeep array length to be packed on current stream.
		/// </summary>
		/// <param name="count">Array length.</param>
		/// <returns>This instance.</returns>
		protected void PackArrayHeaderCore( int count )
		{
			if ( count < 0 )
			{
				throw new ArgumentOutOfRangeException( "count", String.Format( CultureInfo.CurrentCulture, "'{0}' is negative.", "count" ) );
			}

			Contract.EndContractBlock();
			this.VerifyNotDisposed();

			this.PrivatePackArrayHeaderCore( count );
		}

		private void PrivatePackArrayHeaderCore( int count )
		{
			Contract.Assert( 0 <= count );
			if ( count < 16 )
			{
				this.WriteByte( unchecked( ( byte )( MessagePackCode.MinimumFixedArray | count ) ) );
			}
			else if ( count <= UInt16.MaxValue )
			{
				this.WriteByte( MessagePackCode.Array16 );
				unchecked
				{
					this.WriteByte( ( byte )( ( count >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( count & 0xff ) );
				}
			}
			else
			{
				this.WriteByte( MessagePackCode.Array32 );
				this.WriteByte( ( byte )( ( count >> 24 ) & 0xff ) );
				this.WriteByte( ( byte )( ( count >> 16 ) & 0xff ) );
				this.WriteByte( ( byte )( ( count >> 8 ) & 0xff ) );
				this.WriteByte( ( byte )( count & 0xff ) );
			}
		}

		/// <summary>
		///		Bookkeep dictionary items count to be packed on current stream.
		/// </summary>
		/// <param name="count">Dictionary items count.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackMapHeader( int count )
		{
			this.PackMapHeaderCore( count );
			return this;
		}

		/// <summary>
		///		Bookkeep dictionary items count to be packed on current stream.
		/// </summary>
		/// <param name="count">Dictionary items count.</param>
		/// <returns>This instance.</returns>
		protected void PackMapHeaderCore( int count )
		{
			if ( count < 0 )
			{
				throw new ArgumentOutOfRangeException( "count", String.Format( CultureInfo.CurrentCulture, "'{0}' is negative.", "count" ) );
			}

			Contract.EndContractBlock();
			this.VerifyNotDisposed();

			this.PrivatePackMapHeaderCore( count );
		}

		private void PrivatePackMapHeaderCore( int count )
		{
			Contract.Assert( 0 <= count );

			if ( count < 16 )
			{
				this.WriteByte( unchecked( ( byte )( MessagePackCode.MinimumFixedMap | count ) ) );
			}
			else if ( count <= UInt16.MaxValue )
			{
				this.WriteByte( MessagePackCode.Map16 );
				unchecked
				{
					this.WriteByte( ( byte )( ( count >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( count & 0xff ) );
				}
			}
			else
			{
				this.WriteByte( MessagePackCode.Map32 );
				unchecked
				{
					this.WriteByte( ( byte )( ( count >> 24 ) & 0xff ) );
					this.WriteByte( ( byte )( ( count >> 16 ) & 0xff ) );
					this.WriteByte( ( byte )( ( count >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( count & 0xff ) );
				}
			}
		}

		/// <summary>
		///		Bookkeep byte length to be packed on current stream.
		/// </summary>
		/// <param name="length">Byte length.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackRawHeader( int length )
		{
			this.PackRawHeaderCore( length );
			return this;
		}

		/// <summary>
		///		Bookkeep byte length to be packed on current stream.
		/// </summary>
		/// <param name="length">Byte length.</param>
		/// <returns>This instance.</returns>
		protected void PackRawHeaderCore( int length )
		{
			if ( length < 0 )
			{
				throw new ArgumentOutOfRangeException( "length", String.Format( CultureInfo.CurrentCulture, "'{0}' is negative.", "length" ) );
			}

			Contract.EndContractBlock();
			this.VerifyNotDisposed();

			this.PrivatePackRawHeaderCore( length );
		}

		private void PrivatePackRawHeaderCore( int length )
		{
			Contract.Assert( 0 <= length );

			if ( length < 32 )
			{
				this.WriteByte( unchecked( ( byte )( MessagePackCode.MinimumFixedRaw | length ) ) );
			}
			else if ( length <= UInt16.MaxValue )
			{
				this.WriteByte( MessagePackCode.Raw16 );
				unchecked
				{
					this.WriteByte( ( byte )( ( length >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( length & 0xff ) );
				}
			}
			else
			{
				this.WriteByte( MessagePackCode.Raw32 );
				unchecked
				{
					this.WriteByte( ( byte )( ( length >> 24 ) & 0xff ) );
					this.WriteByte( ( byte )( ( length >> 16 ) & 0xff ) );
					this.WriteByte( ( byte )( ( length >> 8 ) & 0xff ) );
					this.WriteByte( ( byte )( length & 0xff ) );
				}
			}
		}

		#endregion -- Collection Header --

		#region -- Raw --

		/// <summary>
		///		Pack specified byte stream to current stream.
		/// </summary>
		/// <param name="value">Source bytes its size is not known.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackRaw( IEnumerable<byte> value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();

			this.PrivatePackRaw( value );
			return this;
		}

		/// <summary>
		///		Pack specified byte stream to current stream.
		/// </summary>
		/// <param name="value">Source bytes its size is known.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackRaw( IList<byte> value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();

			var asByteArray = value as byte[];
			if ( asByteArray == null )
			{
				this.PrivatePackRaw( value );
			}
			else
			{
				this.PrivatePackRaw( asByteArray );
			}
			return this;
		}

		/// <summary>
		///		Pack specified byte array to current stream.
		/// </summary>
		/// <param name="value">Source byte array.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackRaw( byte[] value )
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();

			this.PrivatePackRaw( value );
			return this;
		}

		private void PrivatePackRaw( byte[] value )
		{
			if ( value == null )
			{
				this.PrivatePackNullCore();
				return;
			}

			PrivatePackRawCore( value, false );
		}

		private void PrivatePackRawCore( byte[] value, bool isImmutable )
		{
			this.PrivatePackRawHeaderCore( value.Length );
			this.WriteBytes( value, isImmutable );
		}

		private void PrivatePackRaw( IList<byte> value )
		{
			if ( value == null )
			{
				this.PrivatePackNullCore();
				return;
			}

			this.PrivatePackRawHeaderCore( value.Count );
			this.WriteBytes( value );
		}

		private void PrivatePackRaw( IEnumerable<byte> value )
		{
			if ( value == null )
			{
				this.PrivatePackNullCore();
				return;
			}

			this.PrivatePackRawCore( value );
		}

		private void PrivatePackRawCore( IEnumerable<byte> value )
		{
			if ( !this.CanSeek )
			{
				// buffered
				this.PrivatePackRawCore( value.ToArray(), true );
			}
			else
			{
				// Header
				this.WriteByte( MessagePackCode.Raw32 );
				this.StreamWrite( value, ( items, _ ) => this.PrivatePackRawBodyCore( items ), null );
			}
		}

		/// <summary>
		///		Pack specified byte array to current stream without any header.
		/// </summary>
		/// <param name="value">Source byte array.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		/// <remarks>
		///		If you forget to write header first, then resulting stream will be corrupsed.
		/// </remarks>
		public Packer PackRawBody( byte[] value )
		{
			if ( value == null )
			{
				throw new ArgumentNullException( "value" );
			}

			this.VerifyNotDisposed();
			Contract.EndContractBlock();

			this.WriteBytes( value, false );
			return this;
		}

		/// <summary>
		///		Packs specified byte sequence to current stream without any header.
		/// </summary>
		/// <param name="value">Source byte array.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		/// <remarks>
		///		If you forget to write header first, then resulting stream will be corrupsed.
		/// </remarks>
		public Packer PackRawBody( IEnumerable<byte> value )
		{
			if ( value == null )
			{
				throw new ArgumentNullException( "value" );
			}

			this.VerifyNotDisposed();
			Contract.EndContractBlock();

			this.PrivatePackRawBodyCore( value );
			return this;
		}

		private int PrivatePackRawBodyCore( IEnumerable<byte> value )
		{
			Contract.Assert( value != null );

			var asCollection = value as ICollection<byte>;
			if ( asCollection != null )
			{
				return this.PrivatePackRawBodyCore( asCollection, asCollection.IsReadOnly );
			}

			int bodyLength = 0;

			foreach ( var b in value )
			{
				this.WriteByte( b );
				bodyLength++;
			}

			return bodyLength;
		}

		private int PrivatePackRawBodyCore( ICollection<byte> value, bool isImmutable )
		{
			Contract.Assert( value != null );

			var asArray = value as byte[];
			if ( asArray != null )
			{
				this.WriteBytes( asArray, isImmutable );
			}
			else
			{
				this.WriteBytes( value );
			}

			return value.Count;
		}

		#endregion -- Raw --

		#region -- String --

		/// <summary>
		///		Pack specified char stream to current stream with UTF-8 <see cref="Encoding"/>.
		/// </summary>
		/// <param name="value">Source chars its size is not known.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackString( IEnumerable<char> value )
		{
			this.PackStringCore( value, Encoding.UTF8 );
			return this;
		}

		/// <summary>
		///		Pack specified string to current stream with UTF-8 <see cref="Encoding"/>.
		/// </summary>
		/// <param name="value">Source string.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackString( string value )
		{
			this.PackStringCore( value, Encoding.UTF8 );
			return this;
		}

		/// <summary>
		///		Pack specified char stream to current stream with specified <see cref="Encoding"/>.
		/// </summary>
		/// <param name="value">Source chars its size is not known.</param>
		/// <param name="encoding"><see cref="Encoding"/> to be used.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackString( IEnumerable<char> value, Encoding encoding )
		{
			this.PackStringCore( value, encoding );
			return this;
		}

		/// <summary>
		///		Pack specified string to current stream with specified <see cref="Encoding"/>.
		/// </summary>
		/// <param name="value">Source string.</param>
		/// <param name="encoding"><see cref="Encoding"/> to be used.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackString( string value, Encoding encoding )
		{
			this.PackStringCore( value, encoding );
			return this;
		}

		/// <summary>
		///		Pack specified char stream to current stream with specified <see cref="Encoding"/>.
		/// </summary>
		/// <param name="value">Source chars its size is not known.</param>
		/// <param name="encoding"><see cref="Encoding"/> to be used.</param>
		protected virtual void PackStringCore( IEnumerable<char> value, Encoding encoding )
		{
			if ( encoding == null )
			{
				throw new ArgumentNullException( "encoding" );
			}

			Contract.EndContractBlock();
			this.VerifyNotDisposed();

			this.PrivatePackString( value, encoding );
		}

		private void PrivatePackString( IEnumerable<char> value, Encoding encoding )
		{
			Contract.Assert( encoding != null );

			if ( value == null )
			{
				this.PrivatePackNullCore();
				return;
			}

			this.PrivatePackStringCore( value, encoding );
		}

		private void PrivatePackStringCore( IEnumerable<char> value, Encoding encoding )
		{
			Contract.Assert( value != null );
			Contract.Assert( encoding != null );

			// TODO: streaming encoding
			var encoded = encoding.GetBytes( value.ToArray() );
			this.PrivatePackRawHeaderCore( encoded.Length );
			this.WriteBytes( encoded, true );
		}

		/// <summary>
		///		Pack specified string to current stream with specified <see cref="Encoding"/>.
		/// </summary>
		/// <param name="value">Source string.</param>
		/// <param name="encoding"><see cref="Encoding"/> to be used.</param>
		protected virtual void PackStringCore( string value, Encoding encoding )
		{
			if ( encoding == null )
			{
				throw new ArgumentNullException( "encoding" );
			}

			Contract.EndContractBlock();
			this.VerifyNotDisposed();

			this.PrivatePackString( value, encoding );
		}


		private void PrivatePackString( string value, Encoding encoding )
		{
			Contract.Assert( encoding != null );

			if ( value == null )
			{
				this.PrivatePackNullCore();
				return;
			}

			this.PrivatePackStringCore( value, encoding );
		}

		private void PrivatePackStringCore( string value, Encoding encoding )
		{
			Contract.Assert( value != null );
			Contract.Assert( encoding != null );

			// TODO: streaming encoding
			var encoded = encoding.GetBytes( value );
			this.PrivatePackRawHeaderCore( encoded.Length );
			this.WriteBytes( encoded, true );
		}

		#endregion -- String --

		#region -- List --

		/// <summary>
		///		Bookkeep collection count to be packed on current stream.
		/// </summary>
		/// <param name="array">Collection count to be written.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackArrayHeader<TItem>( IList<TItem> array )
		{
			if ( array == null )
			{
				return this.PackNull();
			}
			else
			{
				return this.PackArrayHeader( array.Count );
			}
		}

		#endregion -- List --

		#region -- IDictionary --

		/// <summary>
		///		Bookkeep dictionary count to be packed on current stream.
		/// </summary>
		/// <param name="map">Dictionary count to be written.</param>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackMapHeader<TKey, TValue>( IDictionary<TKey, TValue> map )
		{
			if ( map == null )
			{
				return this.PackNull();
			}
			else
			{
				return this.PackMapHeader( map.Count );
			}
		}

		#endregion -- IDictionary --

		/// <summary>
		///		Try pack <see cref="SByte"/> value to current stream as tiny fix num.
		/// </summary>
		/// <param name="value">Maybe tiny <see cref="SByte"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		protected bool TryPackTinySignedInteger( long value )
		{
			// positive fixnum
			if ( value >= 0 && value < 128L )
			{
				this.WriteByte( unchecked( ( byte )value ) );
				return true;
			}

			// negative fixnum
			if ( value >= -32L && value <= -1L )
			{
				this.WriteByte( unchecked( ( byte )value ) );
				return true;
			}

			return false;
		}

		/// <summary>
		///		Try pack <see cref="Byte"/> value to current stream as tiny fix num.
		/// </summary>
		/// <param name="value">Maybe tiny <see cref="Byte"/> value.</param>
		/// <returns>If <paramref name="value"/> has be packed successfully then true, otherwise false (normally, larger type required).</returns>
		[CLSCompliant( false )]
		protected bool TryPackTinyUnsignedInteger( ulong value )
		{
			// positive fixnum
			if ( value < 128L )
			{
				this.WriteByte( unchecked( ( byte )value ) );
				return true;
			}

			return false;
		}

		/// <summary>
		///		Pack a null value to current stream.
		/// </summary>
		/// <returns>This instance.</returns>
		/// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
		public Packer PackNull()
		{
			this.VerifyNotDisposed();
			Contract.EndContractBlock();

			this.PrivatePackNullCore();
			return this;
		}

		private void PrivatePackNullCore()
		{
			this.WriteByte( MessagePackCode.NilValue );
		}
	}
}
