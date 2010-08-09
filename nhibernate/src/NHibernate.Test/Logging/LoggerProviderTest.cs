using NUnit.Framework;
using SharpTestsEx;

namespace NHibernate.Test.Logging
{
	public class LoggerProviderTest
	{
		[Test]
		public void LoggerProviderCanCreateLoggers()
		{
			LoggerProvider.LoggerFor("pizza").Should().Not.Be.Null();
			LoggerProvider.LoggerFor(typeof(LoggerProviderTest)).Should().Not.Be.Null();
		}
	}
}