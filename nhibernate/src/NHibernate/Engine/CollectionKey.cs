using System;
using NHibernate.Engine;
using NHibernate.Persister.Collection;
using NHibernate.Type;

namespace NHibernate.Engine
{
	[Serializable]
	public sealed class CollectionKey
	{
		private readonly string role;
		private readonly object key;
		private readonly IType keyType;
		private readonly ISessionFactoryImplementor factory;
		private readonly int hashCode;

		public CollectionKey(ICollectionPersister persister, object key)
			: this(persister.Role, key, persister.KeyType, persister.Factory)
		{
		}

		private CollectionKey(string role, object key, IType keyType, ISessionFactoryImplementor factory)
		{
			this.role = role;
			this.key = key;
			this.keyType = keyType;
			this.factory = factory;
			this.hashCode = GenerateHashCode();
		}

		public override bool Equals(object obj)
		{
			CollectionKey that = (CollectionKey) obj;
			return keyType.IsEqual(key, that.key, EntityMode.Poco) && Equals(role, that.role);
		}

		public override int GetHashCode()
		{
			return hashCode;
		}

		private int GenerateHashCode()
		{
			int result = 17;
			unchecked
			{
				result = 37 * result + role.GetHashCode();
				result = 37 * result + keyType.GetHashCode(key, EntityMode.Poco, factory);
			}
			return result;
		}

		public string Role
		{
			get { return role; }
		}

		public object Key
		{
			get { return key; }
		}
	}
}