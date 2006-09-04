using System;
using System.Text;

using NHibernate.Engine;
using NHibernate.Persister.Collection;
using NHibernate.Persister.Entity;
using NHibernate.SqlCommand;
using NHibernate.Type;

namespace NHibernate.Expression
{
	abstract public class AbstractEmptinessExpression : ICriterion
	{
		readonly TypedValue[] NO_VALUES = new TypedValue[0];
		readonly string propertyName;

		protected abstract bool ExcludeEmpty { get; }


		protected AbstractEmptinessExpression(string propertyName)
		{
			this.propertyName = propertyName;
		}

		public TypedValue[] GetTypedValues(ICriteria criteria, ICriteriaQuery criteriaQuery)
		{
			return NO_VALUES;
		}

		public sealed override string ToString()
		{
			return propertyName + (ExcludeEmpty ? " is not empty" : " is empty");
		}

		public SqlString ToSqlString(ICriteria criteria, ICriteriaQuery criteriaQuery)
		{
			System.Type entityNameType = criteriaQuery.GetEntityName(criteria, propertyName);
            string entityName = entityNameType.Name;
			string actualPropertyName = criteriaQuery.GetPropertyName(propertyName);
			string sqlAlias = criteriaQuery.GetSQLAlias(criteria, propertyName);

			ISessionFactoryImplementor factory = criteriaQuery.Factory;
			IQueryableCollection collectionPersister = GetQueryableCollection(entityName, actualPropertyName, factory);

			string[] collectionKeys = collectionPersister.KeyColumnNames;
			string[] ownerKeys = ((ILoadable)factory.GetEntityPersister(entityName)).IdentifierColumnNames;

			string innerSelect = "(select 1 from " + collectionPersister.TableName
							+ " where "
							+ new ConditionalFragment().SetTableAlias(sqlAlias).SetCondition(ownerKeys, collectionKeys).ToSqlStringFragment()
							+ ")";

            return new SqlString(new string[] {ExcludeEmpty ? "exists" : "not exists", innerSelect});
		}


		protected IQueryableCollection GetQueryableCollection(string entityName, string propertyName, ISessionFactoryImplementor factory)
		{
			IPropertyMapping ownerMapping = (IPropertyMapping)factory.GetEntityPersister(entityName);
			IType type = ownerMapping.ToType(propertyName);
			if (!type.IsCollectionType)
			{
				throw new MappingException(
								"Property path [" + entityName + "." + propertyName + "] does not reference a collection"
				);
			}

			string role = ((CollectionType)type).Role;
			try
			{
				return (IQueryableCollection)factory.GetCollectionPersister(role);
			}
			catch (InvalidCastException cce)
			{
				throw new QueryException("collection role is not queryable: " + role, cce);
			}
			catch (Exception e)
			{
				throw new QueryException("collection role not found: " + role, e);
			}
		}
	}
}