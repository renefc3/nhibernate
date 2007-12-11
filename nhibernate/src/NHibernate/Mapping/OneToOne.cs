using System.Collections.Generic;
using NHibernate.Type;
using NHibernate.Util;

namespace NHibernate.Mapping
{
	/// <summary>
	/// A mapping for a <c>one-to-one</c> association.
	/// </summary>
	public class OneToOne : ToOne
	{
		private bool constrained;
		private ForeignKeyDirection foreignKeyDirection;
		private SimpleValue identifier;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="table"></param>
		/// <param name="identifier"></param>
		public OneToOne(Table table, SimpleValue identifier) : base(table)
		{
			this.identifier = identifier;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="propertyClass"></param>
		/// <param name="propertyName"></param>
		/// <param name="propertyAccess"></param>
		public override void SetTypeByReflection(System.Type propertyClass, string propertyName, string propertyAccess)
		{
			try
			{
				if (Type == null)
				{
					System.Type refClass = ReflectHelper.ReflectedPropertyClass(propertyClass, propertyName, propertyAccess);
					ReferencedEntityName = refClass.FullName;
					Type = TypeFactory.OneToOne(
						refClass,
						foreignKeyDirection,
						ReferencedPropertyName,
						IsLazy, propertyName);
				}
			}
			catch (HibernateException he)
			{
				throw new MappingException("Problem trying to set association type by reflection", he);
			}
		}

		/// <summary></summary>
		public override void CreateForeignKey()
		{
			if (constrained && referencedPropertyName == null)
			{
				//TODO: handle the case of a foreign key to something other than the pk
				CreateForeignKeyOfEntity(((EntityType)Type).GetAssociatedEntityName());
			}
		}

		/// <summary></summary>
		public override IEnumerable<Column> ConstraintColumns
		{
			get { return new SafetyEnumerable<Column>(identifier.ColumnIterator); }
		}

		/// <summary></summary>
		public bool IsConstrained
		{
			get { return constrained; }
			set { constrained = value; }
		}

		/// <summary></summary>
		public ForeignKeyDirection ForeignKeyDirection
		{
			get { return foreignKeyDirection; }
			set { foreignKeyDirection = value; }
		}

		/// <summary></summary>
		public IValue Identifier
		{
			get { return identifier; }
			set { identifier = (SimpleValue) value; }
		}

		/// <summary></summary>
		public override bool IsNullable
		{
			get { return !IsConstrained; }
		}
	}
}