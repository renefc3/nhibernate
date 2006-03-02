#if NET_2_0

using System;
using System.Collections.Generic;

using Iesi.Collections.Generic;

using NUnit.Framework;

namespace Iesi.Collections.Generic.Test
{
	/// <summary>
	/// Summary description for HashedSetFixture.
	/// </summary>
	[TestFixture]
	public class HashedSetFixture : GenericSetFixture
	{

		protected override ISet<string> CreateInstance()
		{
			return new HashedSet<string>();
		}

		protected override ISet<string> CreateInstance(ICollection<string> init)
		{
			return new HashedSet<string>(init);
		}

		protected override Type ExpectedType
		{
			get { return typeof(HashedSet<string>); }
		}

		[Test]
		public void Serialization() 
		{
			// serialize and then deserialize the ISet.
			System.IO.Stream stream = new System.IO.MemoryStream();
			System.Runtime.Serialization.IFormatter formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
			formatter.Serialize( stream, _set );
			stream.Position = 0;

			ISet<string> desSet = (ISet<string>)formatter.Deserialize(stream);
			stream.Close();

			Assert.AreEqual( 3, desSet.Count, "should have des 3 items" );
			Assert.IsTrue( desSet.Contains( one ), "should contain one" );
			Assert.IsTrue( desSet.Contains( two ), "should contain two" );
			Assert.IsTrue( desSet.Contains( three ), "should contain three" );
		}

		
	}
}
#endif
