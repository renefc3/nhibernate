using System;
using NHibernate.Type;
using NHibernate.Engine;

namespace NHibernate.Dialect.Function
{
	/// <summary>
	/// Classic COUNT sqlfunction that return types as it was done in Hibernate 3.1
	/// </summary>
	public class ClassicCountFunction : StandardSQLFunction
	{
		public ClassicCountFunction() : base("count") { }

		public override IType ReturnType(IType columnType, IMapping mapping)
		{
			return NHibernateUtil.Int32;
		}

	}
}