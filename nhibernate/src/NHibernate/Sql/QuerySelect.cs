using System;
using System.Text;
using System.Collections;

using NHibernate.Dialect;
using NHibernate.Util;

namespace NHibernate.Sql 
{
	/// <summary>
	/// Summary description for QuerySelect.
	/// </summary>
	public class QuerySelect 
	{		
		private SqlCommand.JoinFragment joins;
		private StringBuilder select = new StringBuilder();
		private StringBuilder where = new StringBuilder();
		private StringBuilder groupBy = new StringBuilder();
		private StringBuilder orderBy = new StringBuilder();
		private StringBuilder having = new StringBuilder();
		private bool distinct=false;

		private static readonly IList dontSpace = new ArrayList();

		static QuerySelect() 
		{
			//dontSpace.add("'");
			dontSpace.Add(".");
			dontSpace.Add("+");
			dontSpace.Add("-");
			dontSpace.Add("/");
			dontSpace.Add("*");
			dontSpace.Add("<");
			dontSpace.Add(">");
			dontSpace.Add("=");
			dontSpace.Add("#");
			dontSpace.Add("~");
			dontSpace.Add("|");
			dontSpace.Add("&");
			dontSpace.Add("<=");
			dontSpace.Add(">=");
			dontSpace.Add("=>");
			dontSpace.Add("=<");
			dontSpace.Add("!=");
			dontSpace.Add("<>");
			dontSpace.Add("!#");
			dontSpace.Add("!~");
			dontSpace.Add("!<");
			dontSpace.Add("!>");
			dontSpace.Add(StringHelper.OpenParen); //for MySQL
			dontSpace.Add(StringHelper.ClosedParen);
		}

		public QuerySelect(Dialect.Dialect dialect) 
		{
			joins = new SqlCommand.QueryJoinFragment(dialect, false);
		}

		public SqlCommand.JoinFragment JoinFragment 
		{
			get { return joins; }
		}
	
		public void AddSelectFragmentString(string fragment) 
		{
			if ( fragment.Length>0 && fragment[0]==',' ) fragment = fragment.Substring(1);
			fragment = fragment.Trim();
			if ( fragment.Length>0 ) 
			{
				if ( select.Length>0 ) select.Append(StringHelper.CommaSpace);
				select.Append(fragment);
			}
		}
	
		public void AddSelectColumn(string columnName, string alias) 
		{
			AddSelectFragmentString(columnName + ' ' + alias);
		}
	
		public bool Distinct 
		{
			set { this.distinct = value; }
		}
	
		public void SetWhereTokens(ICollection tokens) 
		{
			//if ( conjunctiveWhere.length()>0 ) conjunctiveWhere.append(" and ");
			AppendTokens(where, tokens);
		}
		
		public void SetGroupByTokens(ICollection tokens)
		{
			//if ( groupBy.length()>0 ) groupBy.append(" and ");
			AppendTokens(groupBy, tokens); 
		}
		
		public void SetOrderByTokens(ICollection tokens) 
		{
			//if ( orderBy.length()>0 ) orderBy.append(" and ");
			AppendTokens(orderBy, tokens);
		}
		
		public void SetHavingTokens(ICollection tokens)
		{
			//if ( having.length()>0 ) having.append(" and ");
			AppendTokens(having, tokens); 
		}

		public void AddOrderBy(string orderByString) 
		{
			if( orderBy.Length > 0 ) orderBy.Append(StringHelper.CommaSpace);
			orderBy.Append(orderByString);
		}

		public string ToQueryString() 
		{
			StringBuilder buf = new StringBuilder(50)
				.Append("select ");

			if (distinct) buf.Append("distinct ");
			
			//TODO: HACK with ToString()
			string from = joins.ToFromFragmentString.ToString();
			if ( from.StartsWith(",") ) 
			{
				from = from.Substring(1);
			}
			else if ( from.StartsWith(" inner join") ) 
			{
				from = from.Substring(11);
			}

			buf.Append(select.ToString())
				.Append(" from")
				.Append( from );
			
			//TODO: HACK with ToString()
			string part1 = joins.ToWhereFragmentString.ToString().Trim();
			string part2 = where.ToString().Trim();
			bool hasPart1 = part1.Length > 0;
			bool hasPart2 = part2.Length > 0;
			
			if (hasPart1 || hasPart2) buf.Append(" where ");
			if (hasPart1) buf.Append( part1.Substring(4) );
			if (hasPart2) 
			{
				if (hasPart1) buf.Append(" and (");
				buf.Append(part2);
				if (hasPart1) buf.Append(")");
			}
			if ( groupBy.Length > 0 ) buf.Append(" group by ").Append( groupBy.ToString() );
			if ( having.Length > 0 ) buf.Append(" having ").Append( having.ToString() );
			if ( orderBy.Length > 0 ) buf.Append(" order by ").Append( orderBy.ToString() );
			return buf.ToString();
		}

		private void AppendTokens(StringBuilder buf, ICollection iter) 
		{
			bool lastSpaceable = true;
			bool lastQuoted = false;

			foreach(string token in iter) 
			{
				bool spaceable = !dontSpace.Contains(token);
				bool quoted = token.StartsWith("'");

				if (spaceable && lastSpaceable) 
				{
					if (!quoted || !lastQuoted) buf.Append(' ');
				}

				lastSpaceable = spaceable;
				buf.Append(token);
				lastQuoted = token.EndsWith("'");
			}
		}
	}
}