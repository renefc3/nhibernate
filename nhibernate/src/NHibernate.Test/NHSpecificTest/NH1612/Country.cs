using System.Collections.Generic;

namespace NHibernate.Test.NHSpecificTest.NH1612
{
	public class Country : Area
	{
		public virtual IList<string> Routes { get; private set; }
		public virtual IList<City> Cities { get; private set; }

		protected Country() {}

		public Country(string code, string name) : base(code, name)
		{
			Routes = new List<string>();
			Cities = new List<City>();
		}
	}
}