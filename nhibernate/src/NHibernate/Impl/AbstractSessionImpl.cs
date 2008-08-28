using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Transactions;
using NHibernate.AdoNet;
using NHibernate.Collection;
using NHibernate.Engine;
using NHibernate.Engine.Query;
using NHibernate.Engine.Query.Sql;
using NHibernate.Event;
using NHibernate.Exceptions;
using NHibernate.Hql;
using NHibernate.Loader.Custom;
using NHibernate.Persister.Entity;
using NHibernate.Type;

namespace NHibernate.Impl
{
	using log4net;

	/// <summary> Functionality common to stateless and stateful sessions </summary>
	[Serializable]
	public abstract class AbstractSessionImpl : ISessionImplementor, IEnlistmentNotification
	{
		[NonSerialized]
		private ISessionFactoryImplementor factory;

		private readonly Guid sessionId = Guid.NewGuid();
		private bool closed;
		private System.Transactions.Transaction ambientTransation;
		private bool isAlreadyDisposed;
		protected bool shouldCloseSessionOnDtcTransactionCompleted;

		private static readonly ILog logger = LogManager.GetLogger(typeof(AbstractSessionImpl));

		public Guid SessionId
		{
			get { return sessionId; }
		}

		protected bool TakingPartInDtcTransaction
		{
			get
			{
				return ambientTransation != null;
			}
		}

		internal AbstractSessionImpl() { }

		protected internal AbstractSessionImpl(ISessionFactoryImplementor factory)
		{
			this.factory = factory;
		}

		#region ISessionImplementor Members

		public void Initialize()
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				CheckAndUpdateSessionStatus();
			}
		}

		public abstract void InitializeCollection(IPersistentCollection collection, bool writing);
		public abstract object InternalLoad(string entityName, object id, bool eager, bool isNullable);
		public abstract object ImmediateLoad(string entityName, object id);
		public abstract long Timestamp { get; }

		public ISessionFactoryImplementor Factory
		{
			get { return factory; }
			protected set { factory = value; }
		}
		public abstract EntityMode EntityMode { get; }

		public abstract IBatcher Batcher { get; }

		public abstract IList List(string query, QueryParameters parameters);
		public abstract void List(string query, QueryParameters parameters, IList results);
		public abstract IList<T> List<T>(string query, QueryParameters queryParameters);
		public abstract IList<T> List<T>(CriteriaImpl criteria);
		public abstract void List(CriteriaImpl criteria, IList results);
		public abstract IList List(CriteriaImpl criteria);
		public abstract IEnumerable Enumerable(string query, QueryParameters parameters);
		public abstract IEnumerable<T> Enumerable<T>(string query, QueryParameters queryParameters);
		public abstract IList ListFilter(object collection, string filter, QueryParameters parameters);
		public abstract IList<T> ListFilter<T>(object collection, string filter, QueryParameters parameters);
		public abstract IEnumerable EnumerableFilter(object collection, string filter, QueryParameters parameters);
		public abstract IEnumerable<T> EnumerableFilter<T>(object collection, string filter, QueryParameters parameters);
		public abstract IEntityPersister GetEntityPersister(string entityName, object obj);
		public abstract void AfterTransactionBegin(ITransaction tx);
		public abstract void BeforeTransactionCompletion(ITransaction tx);
		public abstract void AfterTransactionCompletion(bool successful, ITransaction tx);
		public abstract object GetContextEntityIdentifier(object obj);
		public abstract object Instantiate(string clazz, object id);
		public abstract IList List(NativeSQLQuerySpecification spec, QueryParameters queryParameters);
		public abstract void List(NativeSQLQuerySpecification spec, QueryParameters queryParameters, IList results);
		public abstract IList<T> List<T>(NativeSQLQuerySpecification spec, QueryParameters queryParameters);
		public abstract void ListCustomQuery(ICustomQuery customQuery, QueryParameters queryParameters, IList results);
		public abstract IList<T> ListCustomQuery<T>(ICustomQuery customQuery, QueryParameters queryParameters);
		public abstract object GetFilterParameterValue(string filterParameterName);
		public abstract IType GetFilterParameterType(string filterParameterName);
		public abstract ISession GetSession();
		public abstract IDictionary<string, IFilter> EnabledFilters { get; }

		public virtual IQuery GetNamedSQLQuery(string name)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				CheckAndUpdateSessionStatus();
				NamedSQLQueryDefinition nsqlqd = factory.GetNamedSQLQuery(name);
				if (nsqlqd == null)
				{
					throw new MappingException("Named SQL query not known: " + name);
				}
				IQuery query = new SqlQueryImpl(nsqlqd, this,
												factory.QueryPlanCache.GetSQLParameterMetadata(nsqlqd.QueryString));
				query.SetComment("named native SQL query " + name);
				InitQuery(query, nsqlqd);
				return query;
			}
		}

		public abstract IQueryTranslator[] GetQueries(string query, bool scalar);
		public abstract IInterceptor Interceptor { get; }
		public abstract EventListeners Listeners { get; }
		public abstract int DontFlushFromFind { get; }
		public abstract ConnectionManager ConnectionManager { get; set; }
		public abstract bool IsEventSource { get; }
		public abstract object GetEntityUsingInterceptor(EntityKey key);
		public abstract IPersistenceContext PersistenceContext { get; }
		public abstract CacheMode CacheMode { get; set; }
		public abstract bool IsOpen { get; }
		public abstract bool IsConnected { get; }
		public abstract FlushMode FlushMode { get; set; }
		public abstract string FetchProfile { get; set; }
		public abstract string BestGuessEntityName(object entity);
		public abstract string GuessEntityName(object entity);
		public abstract IDbConnection Connection { get; }
		public abstract int ExecuteNativeUpdate(NativeSQLQuerySpecification specification, QueryParameters queryParameters);
		public abstract int ExecuteUpdate(string query, QueryParameters queryParameters);
		public abstract FutureCriteriaBatch FutureCriteriaBatch { get; internal set; }
		public abstract FutureQueryBatch FutureQueryBatch { get; internal set; }

		public virtual IQuery GetNamedQuery(string queryName)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				CheckAndUpdateSessionStatus();
				NamedQueryDefinition nqd = factory.GetNamedQuery(queryName);
				IQuery query;
				if (nqd != null)
				{
					string queryString = nqd.QueryString;
					query = new QueryImpl(queryString, nqd.FlushMode, this,
										  GetHQLQueryPlan(queryString, false).ParameterMetadata);
					query.SetComment("named HQL query " + queryName);
				}
				else
				{
					NamedSQLQueryDefinition nsqlqd = factory.GetNamedSQLQuery(queryName);
					if (nsqlqd == null)
					{
						throw new MappingException("Named query not known: " + queryName);
					}
					query = new SqlQueryImpl(nsqlqd, this,
											 factory.QueryPlanCache.GetSQLParameterMetadata(nsqlqd.QueryString));
					query.SetComment("named native SQL query " + queryName);
					nqd = nsqlqd;
				}
				InitQuery(query, nqd);
				return query;
			}
		}

		public bool IsClosed
		{
			get { return closed; }
		}

		protected internal virtual void CheckAndUpdateSessionStatus()
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				ErrorIfClosed();
				EnlistInAmbientTransactionIfNeeded();
			}
		}

		protected internal virtual void ErrorIfClosed()
		{
			if (IsClosed || IsAlreadyDisposed)
			{
				throw new ObjectDisposedException("ISession", "Session is closed!");
			}
		}

		protected bool IsAlreadyDisposed
		{
			get { return isAlreadyDisposed; }
			set { isAlreadyDisposed = value; }
		}

		public abstract void Flush();

		public abstract bool TransactionInProgress { get; }

		#endregion

		protected internal void SetClosed()
		{
			try
			{
				if (ambientTransation != null)
					ambientTransation.Dispose();
			}
			catch (Exception)
			{
				//ignore
			}
			closed = true;
		}

		private void InitQuery(IQuery query, NamedQueryDefinition nqd)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				query.SetCacheable(nqd.IsCacheable);
				query.SetCacheRegion(nqd.CacheRegion);
				if (nqd.Timeout != -1)
				{
					query.SetTimeout(nqd.Timeout);
				}
				if (nqd.FetchSize != -1)
				{
					query.SetFetchSize(nqd.FetchSize);
				}
				if (nqd.CacheMode.HasValue)
					query.SetCacheMode(nqd.CacheMode.Value);

				query.SetReadOnly(nqd.IsReadOnly);
				if (nqd.Comment != null)
				{
					query.SetComment(nqd.Comment);
				}
				query.SetFlushMode(nqd.FlushMode);
			}
		}

		public virtual IQuery CreateQuery(string queryString)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				CheckAndUpdateSessionStatus();
				QueryImpl query = new QueryImpl(queryString, this, GetHQLQueryPlan(queryString, false).ParameterMetadata);
				query.SetComment(queryString);
				return query;
			}
		}

		public virtual ISQLQuery CreateSQLQuery(string sql)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				CheckAndUpdateSessionStatus();
				SqlQueryImpl query = new SqlQueryImpl(sql, this, factory.QueryPlanCache.GetSQLParameterMetadata(sql));
				query.SetComment("dynamic native SQL query");
				return query;
			}
		}

		protected internal virtual HQLQueryPlan GetHQLQueryPlan(string query, bool shallow)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				return factory.QueryPlanCache.GetHQLQueryPlan(query, shallow, EnabledFilters);
			}
		}

		protected internal virtual NativeSQLQueryPlan GetNativeSQLQueryPlan(NativeSQLQuerySpecification spec)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				return factory.QueryPlanCache.GetNativeSQLQueryPlan(spec);
			}
		}

		protected ADOException Convert(Exception sqlException, string message)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				return ADOExceptionHelper.Convert(factory.SQLExceptionConverter, sqlException, message);
			}
		}

		protected void AfterOperation(bool success)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				if (!ConnectionManager.IsInActiveTransaction)
				{
					ConnectionManager.AfterNonTransactionalQuery(success);
				}
			}
		}

		#region IEnlistmentNotification Members

		void IEnlistmentNotification.Prepare(PreparingEnlistment preparingEnlistment)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				try
				{
					using (var tx = new TransactionScope(ambientTransation))
					{
						BeforeTransactionCompletion(null);
						if (FlushMode != FlushMode.Never && ConnectionManager.IsConnected)
						{
							using (ConnectionManager.FlushingFromDtcTransaction)
							{
								logger.Debug(string.Format("[session-id={0}] Flushing from Dtc Transaction", sessionId));
								Flush();
							}
						}
						logger.Debug("prepared for DTC transaction");

						tx.Complete();
					}
					preparingEnlistment.Prepared();
				}
				catch (Exception exception)
				{
					logger.Error("DTC transaction prepre phase failed", exception);
					preparingEnlistment.ForceRollback(exception);
				}
			}
		}

		void IEnlistmentNotification.Commit(Enlistment enlistment)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				logger.Debug("committing DTC transaction");
				// we have nothing to do here, since it is the actual 
				// DB connection that will commit the transaction
				enlistment.Done();
			}
		}

		void IEnlistmentNotification.Rollback(Enlistment enlistment)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				AfterTransactionCompletion(false, null);
				logger.Debug("rolled back DTC transaction");
				enlistment.Done();
			}
		}

		void IEnlistmentNotification.InDoubt(Enlistment enlistment)
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				AfterTransactionCompletion(false, null);
				logger.Debug("DTC transaction is in doubt");
				enlistment.Done();
			}
		}

		protected void EnlistInAmbientTransactionIfNeeded()
		{
			using (new SessionIdLoggingContext(SessionId))
			{
				if (ambientTransation != null)
					return;
				if (System.Transactions.Transaction.Current == null)
					return;
				ambientTransation = System.Transactions.Transaction.Current.Clone();
				logger.DebugFormat("enlisted into DTC transaction: {0}", ambientTransation.IsolationLevel);
				AfterTransactionBegin(null);
				ambientTransation.TransactionCompleted += delegate(object sender, TransactionEventArgs e)
				                                          	{
																											bool wasSuccessful = false;
																											try
																											{
																												wasSuccessful = e.Transaction.TransactionInformation.Status
																																				== TransactionStatus.Committed;
																											}
																											catch (ObjectDisposedException ode)
																											{
																												logger.Warn("Completed transaction was disposed.", ode);
																											}
				                                          		AfterTransactionCompletion(wasSuccessful, null);
				                                          		if (shouldCloseSessionOnDtcTransactionCompleted)
				                                          		{
				                                          			Dispose(true);
				                                          		}
				                                          		ambientTransation = null;
				                                          	};
				ambientTransation.EnlistVolatile(this, EnlistmentOptions.EnlistDuringPrepareRequired);
			}
		}

		protected abstract void Dispose(bool disposing);

		#endregion
	}
}
