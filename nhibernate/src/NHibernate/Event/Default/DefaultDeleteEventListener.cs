using System;
using Iesi.Collections;
using log4net;
using NHibernate.Action;
using NHibernate.Classic;
using NHibernate.Engine;
using NHibernate.Impl;
using NHibernate.Persister.Entity;
using NHibernate.Type;
using NHibernate.Util;
using Status=NHibernate.Engine.Status;

namespace NHibernate.Event.Default
{
	/// <summary> 
	/// Defines the default delete event listener used by hibernate for deleting entities
	/// from the datastore in response to generated delete events. 
	/// </summary>
	[Serializable]
	public class DefaultDeleteEventListener : IDeleteEventListener
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(DefaultDeleteEventListener));

		#region IDeleteEventListener Members

		/// <summary>Handle the given delete event. </summary>
		/// <param name="event">The delete event to be handled. </param>
		public void OnDelete(DeleteEvent @event)
		{
			OnDelete(@event, new IdentitySet());
		}

		public void OnDelete(DeleteEvent @event, ISet transientEntities)
		{
			IEventSource source = @event.Session;
			IPersistenceContext persistenceContext = source.PersistenceContext;
			object entity = persistenceContext.UnproxyAndReassociate(@event.Entity);

			EntityEntry entityEntry = persistenceContext.GetEntry(entity);
			IEntityPersister persister;
			object id;
			object version;

			if (entityEntry == null)
			{
				log.Debug("entity was not persistent in delete processing");

				persister = source.GetEntityPersister(entity);
				if (ForeignKeys.IsTransient(persister.EntityName, entity, null, source))
				{
					DeleteTransientEntity(source, entity, @event.CascadeDeleteEnabled, persister, transientEntities);
					// EARLY EXIT!!!
					return;
				}
				else
				{
					PerformDetachedEntityDeletionCheck(@event);
				}

				id = persister.GetIdentifier(entity);

				if (id == null)
				{
					throw new TransientObjectException("the detached instance passed to delete() had a null identifier");
				}

				EntityKey key = new EntityKey(id, persister);

				persistenceContext.CheckUniqueness(key, entity);

				new OnUpdateVisitor(source, id, entity).Process(entity, persister);

				version = persister.GetVersion(entity);

				entityEntry = persistenceContext.AddEntity(entity, Status.Loaded, persister.GetPropertyValues(entity), key, version, LockMode.None, true, persister, false, false);
			}
			else
			{
				log.Debug("deleting a persistent instance");

				if (entityEntry.Status == Status.Deleted || entityEntry.Status == Status.Gone)
				{
					log.Debug("object was already deleted");
					return;
				}
				persister = entityEntry.Persister;
				id = entityEntry.Id;
				version = entityEntry.Version;
			}

			/*if ( !persister.isMutable() ) {
			throw new HibernateException(
			"attempted to delete an object of immutable class: " +
			MessageHelper.infoString(persister)
			);
			}*/

			if (InvokeDeleteLifecycle(source, entity, persister))
			{
				return;
			}

			DeleteEntity(source, entity, entityEntry, @event.CascadeDeleteEnabled, persister, transientEntities);

			// TODO H3.2 Not ported
			//if (source.Factory.Settings.IsIdentifierRollbackEnabled)
			//{
			//  persister.ResetIdentifier(entity, id, version);
			//}
		}

		#endregion
		/// <summary> Called when we have recognized an attempt to delete a detached entity.
		/// <p/>
		/// This is perfectly valid in Hibernate usage; JPA, however, forbids this.
		/// Thus, this is a hook for HEM to affect this behavior.
		/// 
		/// </summary>
		/// <param name="event">The event.
		/// </param>
		protected internal void PerformDetachedEntityDeletionCheck(DeleteEvent @event)
		{
			// ok in normal Hibernate usage to delete a detached entity; JPA however
			// forbids it, thus this is a hook for HEM to affect this behavior
		}

		/// <summary> 
		/// We encountered a delete request on a transient instance.
		/// <p/>
		/// This is a deviation from historical Hibernate (pre-3.2) behavior to
		/// align with the JPA spec, which states that transient entities can be
		/// passed to remove operation in which case cascades still need to be
		/// performed.
		///  </summary>
		/// <param name="session">The session which is the source of the event </param>
		/// <param name="entity">The entity being delete processed </param>
		/// <param name="cascadeDeleteEnabled">Is cascading of deletes enabled</param>
		/// <param name="persister">The entity persister </param>
		/// <param name="transientEntities">
		/// A cache of already visited transient entities (to avoid infinite recursion).
		/// </param>
		protected internal void DeleteTransientEntity(IEventSource session, object entity, bool cascadeDeleteEnabled, IEntityPersister persister, ISet transientEntities)
		{
			log.Info("handling transient entity in delete processing");
			if (transientEntities.Contains(entity))
			{
				log.Debug("already handled transient entity; skipping");
				return;
			}
			transientEntities.Add(entity);
			CascadeBeforeDelete(session, persister, entity, null, transientEntities);
			CascadeAfterDelete(session, persister, entity, transientEntities);
		}

		/// <summary> 
		/// Perform the entity deletion.  Well, as with most operations, does not
		/// really perform it; just schedules an action/execution with the
		/// <see cref="ActionQueue"/> for execution during flush. 
		/// </summary>
		/// <param name="session">The originating session </param>
		/// <param name="entity">The entity to delete </param>
		/// <param name="entityEntry">The entity's entry in the <see cref="ISession"/> </param>
		/// <param name="isCascadeDeleteEnabled">Is delete cascading enabled? </param>
		/// <param name="persister">The entity persister. </param>
		/// <param name="transientEntities">A cache of already deleted entities. </param>
		protected internal void DeleteEntity(IEventSource session, object entity, EntityEntry entityEntry, bool isCascadeDeleteEnabled, IEntityPersister persister, ISet transientEntities)
		{

			if (log.IsDebugEnabled)
			{
				log.Debug("deleting " + MessageHelper.InfoString(persister, entityEntry.Id, session.Factory));
			}

			IPersistenceContext persistenceContext = session.PersistenceContext;

			IType[] propTypes = persister.PropertyTypes;
			object version = entityEntry.Version;

			object[] currentState;
			if (entityEntry.LoadedState == null)
			{
				//ie. the entity came in from update()
				currentState = persister.GetPropertyValues(entity);
			}
			else
			{
				currentState = entityEntry.LoadedState;
			}

			object[] deletedState = CreateDeletedState(persister, currentState, session);
			entityEntry.DeletedState = deletedState;

			session.Interceptor.OnDelete(entity, entityEntry.Id, deletedState, persister.PropertyNames, propTypes);

			// before any callbacks, etc, so subdeletions see that this deletion happened first
			persistenceContext.SetEntryStatus(entityEntry, Status.Deleted);
			EntityKey key = new EntityKey(entityEntry.Id, persister);

			CascadeBeforeDelete(session, persister, entity, entityEntry, transientEntities);

			new ForeignKeys.Nullifier(entity, true, false, session).NullifyTransientReferences(entityEntry.DeletedState, propTypes);
			new Nullability(session).CheckNullability(entityEntry.DeletedState, persister, true);
			persistenceContext.NullifiableEntityKeys.Add(key);

			// Ensures that containing deletions happen before sub-deletions
			session.ActionQueue.AddAction(new EntityDeleteAction(entityEntry.Id, deletedState, version, entity, persister, isCascadeDeleteEnabled, session));

			CascadeAfterDelete(session, persister, entity, transientEntities);

			// the entry will be removed after the flush, and will no longer
			// override the stale snapshot
			// This is now handled by removeEntity() in EntityDeleteAction
			//persistenceContext.removeDatabaseSnapshot(key);
		}

		private object[] CreateDeletedState(IEntityPersister persister, object[] currentState, IEventSource session)
		{
			IType[] propTypes = persister.PropertyTypes;
			object[] deletedState = new object[propTypes.Length];
			//		TypeFactory.deepCopy( currentState, propTypes, persister.getPropertyUpdateability(), deletedState, session );
			bool[] copyability = new bool[propTypes.Length];
			ArrayHelper.Fill(copyability, true);
			TypeFactory.DeepCopy(currentState, propTypes, copyability, deletedState, session);
			return deletedState;
		}

		protected internal bool InvokeDeleteLifecycle(IEventSource session, object entity, IEntityPersister persister)
		{
			if (persister.ImplementsLifecycle)
			{
				log.Debug("calling onDelete()");
				if (((ILifecycle)entity).OnDelete(session) == LifecycleVeto.Veto)
				{
					log.Debug("deletion vetoed by onDelete()");
					return true;
				}
			}
			return false;
		}

		protected internal void CascadeBeforeDelete(IEventSource session, IEntityPersister persister, object entity, EntityEntry entityEntry, ISet transientEntities)
		{
			ISessionImplementor si = session;
			CacheMode cacheMode = si.CacheMode;
			si.CacheMode = CacheMode.Get;
			session.PersistenceContext.IncrementCascadeLevel();
			try
			{
				// cascade-delete to collections BEFORE the collection owner is deleted
				Cascades.Cascade(session, persister, entity, Cascades.CascadingAction.ActionDelete,
							 CascadePoint.CascadeAfterInsertBeforeDelete, null);
			}
			finally
			{
				session.PersistenceContext.DecrementCascadeLevel();
				si.CacheMode = cacheMode;
			}
		}

		protected internal void CascadeAfterDelete(IEventSource session, IEntityPersister persister, object entity, ISet transientEntities)
		{
			ISessionImplementor si = session;
			CacheMode cacheMode = si.CacheMode;
			si.CacheMode = CacheMode.Get;
			session.PersistenceContext.IncrementCascadeLevel();
			try
			{
				// cascade-delete to many-to-one AFTER the parent was deleted
				Cascades.Cascade(session, persister, entity, Cascades.CascadingAction.ActionDelete,
								 CascadePoint.CascadeBeforeInsertAfterDelete);
			}
			finally
			{
				session.PersistenceContext.DecrementCascadeLevel();
				si.CacheMode = cacheMode;
			}
		}
	}
}
