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
using System.Diagnostics;
using System.Globalization;
using System.IO;
#if !NETFX_CORE
using MsgPack.Serialization.EmittingSerializers;
#endif
using MsgPack.Serialization.ExpressionSerializers;
#if !MSTEST
using NUnit.Framework;
#else
using TestFixtureAttribute = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestClassAttribute;
using TestAttribute = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestMethodAttribute;
using TimeoutAttribute = NUnit.Framework.TimeoutAttribute;
using Assert = NUnit.Framework.Assert;
using Is = NUnit.Framework.Is;
#endif

namespace MsgPack.Serialization
{
	[TestFixture]
	public class CompositeTest
	{
#if !NETFX_CORE
		private static bool _traceOn = false;

		[SetUp]
		public void SetUp()
		{
			if ( _traceOn )
			{
				Tracer.Emit.Listeners.Clear();
				Tracer.Emit.Switch.Level = SourceLevels.All;
				Tracer.Emit.Listeners.Add( new ConsoleTraceListener() );
			}

			SerializationMethodGeneratorManager.DefaultSerializationMethodGeneratorOption = SerializationMethodGeneratorOption.CanDump;
		}

		[TearDown]
		public void TearDown()
		{
			if ( _traceOn )
			{
				try
				{
					DefaultSerializationMethodGeneratorManager.DumpTo();
				}
				finally
				{
					DefaultSerializationMethodGeneratorManager.Refresh();
				}
			}
		}

		[Test]
		public void TestArrayFieldBased()
		{
			TestCore( EmitterFlavor.FieldBased, SerializationMethod.Array, c => new ArrayEmittingSerializerBuilder<DirectoryItem>( c ) );
		}

		[Test]
		public void TestMapFieldBased()
		{
			TestCore( EmitterFlavor.FieldBased, SerializationMethod.Map, c => new MapEmittingSerializerBuilder<DirectoryItem>( c ) );
		}

		[Test]
		public void TestArrayContextBased()
		{
			TestCore( EmitterFlavor.ContextBased, SerializationMethod.Array, c => new ArrayEmittingSerializerBuilder<DirectoryItem>( c ) );
		}

		[Test]
		public void TestMapContextBased()
		{
			TestCore( EmitterFlavor.ContextBased, SerializationMethod.Map, c => new MapEmittingSerializerBuilder<DirectoryItem>( c ) );
		}
#endif

		[Test]
		public void TestArrayExpressionBased()
		{
			TestCore( EmitterFlavor.ExpressionBased, SerializationMethod.Array, c => new ExpressionSerializerBuilder<DirectoryItem>( c ) );
		}

		[Test]
		public void TestMapExpressionBased()
		{
			TestCore( EmitterFlavor.ExpressionBased, SerializationMethod.Map, c => new ExpressionSerializerBuilder<DirectoryItem>( c ) );
		}

		private static void TestCore( EmitterFlavor emittingFlavor, SerializationMethod serializationMethod, Func<SerializationContext, SerializerBuilder<DirectoryItem>> builderProvider )
		{
			var root = new DirectoryItem() { Name = "/" };
			root.Directories =
				new[]
				{
					new DirectoryItem() { Name = "tmp/" },
					new DirectoryItem() 
					{ 
						Name = "var/", 
						Directories = new DirectoryItem[ 0 ], 
						Files = new []{ new FileItem(){ Name = "system.log" } }
					}
				};
			root.Files = new FileItem[ 0 ];

			var serializer = new AutoMessagePackSerializer<DirectoryItem>( new SerializationContext() { EmitterFlavor = emittingFlavor, SerializationMethod = serializationMethod, GeneratorOption = SerializationMethodGeneratorOption.CanDump }, builderProvider );
			using ( var memoryStream = new MemoryStream() )
			{
				serializer.Pack( memoryStream, root );

				memoryStream.Position = 0;
				var result = serializer.Unpack( memoryStream );
				Assert.That( result.Name, Is.EqualTo( "/" ) );
				Assert.That( result.Files, Is.Not.Null.And.Empty );
				Assert.That( result.Directories, Is.Not.Null.And.Length.EqualTo( 2 ) );
				Assert.That( result.Directories[ 0 ], Is.Not.Null );
				Assert.That( result.Directories[ 0 ].Name, Is.EqualTo( "tmp/" ) );
				Assert.That( result.Directories[ 0 ].Files, Is.Null );
				Assert.That( result.Directories[ 0 ].Directories, Is.Null );
				Assert.That( result.Directories[ 1 ], Is.Not.Null );
				Assert.That( result.Directories[ 1 ].Name, Is.EqualTo( "var/" ) );
				Assert.That( result.Directories[ 1 ].Files, Is.Not.Null.And.Length.EqualTo( 1 ) );
				Assert.That( result.Directories[ 1 ].Files[ 0 ], Is.Not.Null );
				Assert.That( result.Directories[ 1 ].Files[ 0 ].Name, Is.EqualTo( "system.log" ) );
				Assert.That( result.Directories[ 1 ].Directories, Is.Not.Null.And.Empty );
			}
		}
	}

	public abstract class FileSystemItem
	{
		public string Name { get; set; }
	}

	public sealed class FileItem : FileSystemItem
	{
		public override string ToString()
		{
			return String.Format( CultureInfo.InvariantCulture, "FileItem {{ Path = \"{0}\" }}", this.Name ?? "(null)" );
		}
	}

	public sealed class DirectoryItem : FileSystemItem
	{
		public FileItem[] Files { get; set; }
		public DirectoryItem[] Directories { get; set; }

		public override string ToString()
		{
			return
				String.Format(
					CultureInfo.InvariantCulture,
					"DirecgtoryItem {{ Path = \"{0}\", Directories = {1}, Files = {2} }}",
					this.Name ?? "(null)",
					this.Directories == null ? "(null)" : this.Directories.Length.ToString( CultureInfo.InvariantCulture ),
					this.Files == null ? "(null)" : this.Files.Length.ToString( CultureInfo.InvariantCulture )
				);
		}
	}
}
