using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Iesi.Collections;
using Iesi.Collections.Generic;

using NHibernate.Cache;
using NHibernate.Driver;
using NHibernate.Engine;
using NHibernate.Criterion;
using NHibernate.Loader.Criteria;
using NHibernate.SqlCommand;
using NHibernate.SqlTypes;
using NHibernate.Transform;
using NHibernate.Type;

namespace NHibernate.Impl
{
	public class MultiCriteriaImpl : IMultiCriteria
	{
		private static readonly ILogger log = LoggerProvider.LoggerFor(typeof(MultiCriteriaImpl));
		private readonly IList<ICriteria> criteriaQueries = new List<ICriteria>();
		private readonly IList<System.Type> resultCollectionGenericType = new List<System.Type>();

		private readonly SessionImpl session;
		private readonly ISessionFactoryImplementor factory;
		private readonly List<CriteriaQueryTranslator> translators = new List<CriteriaQueryTranslator>();
		private readonly List<QueryParameters> parameters = new List<QueryParameters>();
		private readonly List<SqlType> types = new List<SqlType>();
		private SqlString sqlString = new SqlString();
		private readonly List<CriteriaLoader> loaders = new List<CriteriaLoader>();
		private readonly Dialect.Dialect dialect;
		private IList criteriaResults;
		private readonly Dictionary<string, int> criteriaResultPositions = new Dictionary<string, int>();
		private bool isCacheable = false;
		private bool forceCacheRefresh = false;
		private string cacheRegion;
		private IResultTransformer resultTransformer;
		private readonly Dictionary<CriteriaLoader, int> loaderToResultIndex = new Dictionary<CriteriaLoader, int>();

		/// <summary>
		/// Initializes a new instance of the <see cref="MultiCriteriaImpl"/> class.
		/// </summary>
		/// <param name="session">The session.</param>
		/// <param name="factory">The factory.</param>
		internal MultiCriteriaImpl(SessionImpl session, ISessionFactoryImplementor factory)
		{
			IDriver driver = session.Factory.ConnectionProvider.Driver;
			if (!driver.SupportsMultipleQueries)
			{
				throw new NotSupportedException(
					string.Format("The driver {0} does not support multiple queries.", driver.GetType().FullName));
			}
			dialect = session.Factory.Dialect;
			this.session = session;
			this.factory = factory;
		}


		public SqlString SqlString
		{
			get { return sqlString; }
		}

		public IList List()
		{
			using (new SessionIdLoggingContext(session.SessionId))
			{
				bool cacheable = session.Factory.Settings.IsQueryCacheEnabled && isCacheable;

				CreateCriteriaLoaders();
				CombineCriteriaQueries();

				if (log.IsDebugEnabled)
				{
					log.DebugFormat("Multi criteria with {0} criteria queries.", criteriaQueries.Count);
					for (int i = 0; i < criteriaQueries.Count; i++)
					{
						log.DebugFormat("Query #{0}: {1}", i, criteriaQueries[i]);
					}
				}

				if (cacheable)
				{
					criteriaResults = ListUsingQueryCache();
				}
				else
				{
					criteriaResults = ListIgnoreQueryCache();
				}

				return criteriaResults;
			}
		}


		private IList ListUsingQueryCache()
		{
			IQueryCache queryCache = session.Factory.GetQueryCache(cacheRegion);

			ISet filterKeys = FilterKey.CreateFilterKeys(session.EnabledFilters, session.EntityMode);

			ISet<string> querySpaces = new HashedSet<string>();
			List<IType[]> resultTypesList = new List<IType[]>();
			int[] maxRows = new int[loaders.Count];
			int[] firstRows = new int[loaders.Count];
			for (int i = 0; i < loaders.Count; i++)
			{
				querySpaces.AddAll(loaders[i].QuerySpaces);
				resultTypesList.Add(loaders[i].ResultTypes);
				firstRows[i] = parameters[i].RowSelection.FirstRow;
				maxRows[i] = parameters[i].RowSelection.MaxRows;
			}

			MultipleQueriesCacheAssembler assembler = new MultipleQueriesCacheAssembler(resultTypesList);
			QueryParameters combinedParameters = CreateCombinedQueryParameters();
			QueryKey key = new QueryKey(session.Factory, SqlString, combinedParameters, filterKeys)
				.SetFirstRows(firstRows)
				.SetMaxRows(maxRows);

			IList result =
				assembler.GetResultFromQueryCache(session,
												  combinedParameters,
												  querySpaces,
												  queryCache,
												  key);

			if (result == null)
			{
				log.Debug("Cache miss for multi criteria query");
				IList list = DoList();
				queryCache.Put(key, new ICacheAssembler[] { assembler }, new object[] { list }, combinedParameters.NaturalKeyLookup, session);
				result = list;
			}

			return GetResultList(result);
		}

		private IList ListIgnoreQueryCache()
		{
			return GetResultList(DoList());
		}

		protected virtual IList GetResultList(IList results)
		{
			if (resultTransformer != null)
			{
				for (int i = 0; i < results.Count; i++)
				{
					results[i] = resultTransformer.TransformList((IList)results[i]);
				}
			}
			else
			{
				for (int i = 0; i < results.Count; i++)
				{
					CriteriaImpl critImp = criteriaQueries[i] as CriteriaImpl;
					if (critImp == null || critImp.ResultTransformer == null)
						continue;
					results[i] = critImp.ResultTransformer.TransformList((IList)results[i]);
				}
			}
			return results;
		}

		private IList DoList()
		{
			List<IList> results = new List<IList>();
			GetResultsFromDatabase(results);
			return results;
		}

		private void CombineCriteriaQueries()
		{
			foreach (CriteriaLoader loader in loaders)
			{
				CriteriaQueryTranslator translator = loader.Translator;
				translators.Add(translator);
				QueryParameters queryParameters = translator.GetQueryParameters();
				parameters.Add(queryParameters);
				SqlCommandInfo commandInfo = loader.GetQueryStringAndTypes(session, queryParameters, types.Count);
				sqlString = sqlString.Append(commandInfo.Text)
					.Append(session.Factory.ConnectionProvider.Driver.MultipleQueriesSeparator)
					.Append(Environment.NewLine);
				types.AddRange(commandInfo.ParameterTypes);
			}
		}

		private void GetResultsFromDatabase(IList results)
		{
			bool statsEnabled = session.Factory.Statistics.IsStatisticsEnabled;
			var stopWatch = new Stopwatch();
			if (statsEnabled)
			{
				stopWatch.Start();
			}
			int rowCount = 0;

			using (
				IDbCommand command =
					session.Batcher.PrepareCommand(CommandType.Text, sqlString, types.ToArray()))
			{
				BindParameters(command);
				ArrayList[] hydratedObjects = new ArrayList[loaders.Count];
				List<EntityKey[]>[] subselectResultKeys = new List<EntityKey[]>[loaders.Count];
				bool[] createSubselects = new bool[loaders.Count];
				IDataReader reader = session.Batcher.ExecuteReader(command);
				try
				{
					for (int i = 0; i < loaders.Count; i++)
					{
						CriteriaLoader loader = loaders[i];
						int entitySpan = loader.EntityPersisters.Length;
						hydratedObjects[i] = entitySpan == 0 ? null : new ArrayList(entitySpan);
						EntityKey[] keys = new EntityKey[entitySpan];
						QueryParameters queryParameters = parameters[i];
						IList tmpResults;
						if (resultCollectionGenericType[i] == typeof(object))
						{
							tmpResults = new ArrayList();
						}
						else
						{
							tmpResults = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(resultCollectionGenericType[i]));
						}

						RowSelection selection = parameters[i].RowSelection;
						createSubselects[i] = loader.IsSubselectLoadingEnabled;
						subselectResultKeys[i] = createSubselects[i] ? new List<EntityKey[]>() : null;
						int maxRows = Loader.Loader.HasMaxRows(selection) ? selection.MaxRows : int.MaxValue;
						if (!dialect.SupportsLimitOffset || !NHibernate.Loader.Loader.UseLimit(selection, dialect))
						{
							Loader.Loader.Advance(reader, selection);
						}
						int count;
						for (count = 0; count < maxRows && reader.Read(); count++)
						{
							rowCount++;

							object o =
								loader.GetRowFromResultSet(reader, session, queryParameters, loader.GetLockModes(queryParameters.LockModes),
														   null, hydratedObjects[i], keys, true);
							if (createSubselects[i])
							{
								subselectResultKeys[i].Add(keys);
								keys = new EntityKey[entitySpan]; //can't reuse in this case
							}
							tmpResults.Add(o);
						}
						results.Add(tmpResults);
						reader.NextResult();
					}
				}
				catch (Exception e)
				{
					log.Error("Error executing multi criteria : [" + command.CommandText + "]");
					throw new HibernateException("Error executing multi criteria : [" + command.CommandText + "]", e);
				}
				finally
				{
					session.Batcher.CloseCommand(command, reader);
				}
				for (int i = 0; i < loaders.Count; i++)
				{
					CriteriaLoader loader = loaders[i];
					loader.InitializeEntitiesAndCollections(hydratedObjects[i], reader, session, false);

					if (createSubselects[i])
					{
						loader.CreateSubselects(subselectResultKeys[i], parameters[i], session);
					}
				}
			}
			if (statsEnabled)
			{
				stopWatch.Stop();
				session.Factory.StatisticsImplementor.QueryExecuted(string.Format("{0} queries (MultiCriteria)", loaders.Count), rowCount, stopWatch.Elapsed);
			}
		}

		private void CreateCriteriaLoaders()
		{
			//a criteria can use more than a single query (polymorphic queries), need to have a 
			//way to correlate a loader to a result index
			int criteriaIndex = 0;
			foreach (CriteriaImpl criteria in criteriaQueries)
			{
				string[] implementors = factory.GetImplementors(criteria.EntityOrClassName);
				int size = implementors.Length;

				CriteriaLoader[] tmpLoaders = new CriteriaLoader[size];
				ISet<string> spaces = new HashedSet<string>();

				for (int i = 0; i < size; i++)
				{
					CriteriaLoader loader = new CriteriaLoader(
						session.GetOuterJoinLoadable(implementors[i]),
						factory,
						criteria,
						implementors[i],
						session.EnabledFilters
						);
					tmpLoaders[i] = loader;
					loaderToResultIndex[loader] = criteriaIndex;
					spaces.AddAll(tmpLoaders[i].QuerySpaces);
				}
				loaders.AddRange(tmpLoaders);
				criteriaIndex += 1;
			}
		}

		private void BindParameters(IDbCommand command)
		{
			int colIndex = 0;

			for (int queryIndex = 0; queryIndex < loaders.Count; queryIndex++)
			{
				int limitParameterSpan = BindLimitParametersFirstIfNeccesary(command, queryIndex, colIndex);
				colIndex = BindQueryParameters(command, queryIndex, colIndex + limitParameterSpan);
				BindLimitParametersLastIfNeccesary(command, queryIndex, colIndex);
			}
		}

		private void BindLimitParametersLastIfNeccesary(IDbCommand command, int queryIndex, int colIndex)
		{
			QueryParameters parameter = parameters[queryIndex];
			RowSelection selection = parameter.RowSelection;
			if (Loader.Loader.UseLimit(selection, dialect) && !dialect.BindLimitParametersFirst)
			{
				Loader.Loader.BindLimitParameters(command, colIndex, selection, session);
			}
		}

		private int BindQueryParameters(IDbCommand command, int queryIndex, int colIndex)
		{
			QueryParameters parameter = parameters[queryIndex];
			colIndex += parameter.BindParameters(command, colIndex, session);
			return colIndex;
		}

		private int BindLimitParametersFirstIfNeccesary(IDbCommand command, int queryIndex, int colIndex)
		{
			int limitParametersSpan = 0;
			QueryParameters parameter = parameters[queryIndex];
			RowSelection selection = parameter.RowSelection;
			if (Loader.Loader.UseLimit(selection, dialect) && dialect.BindLimitParametersFirst)
			{
				limitParametersSpan += Loader.Loader.BindLimitParameters(command, colIndex, selection, session);
			}
			return limitParametersSpan;
		}

		public IMultiCriteria Add(System.Type resultGenericListType, ICriteria criteria)
		{
			criteriaQueries.Add(criteria);
			resultCollectionGenericType.Add(resultGenericListType);

			return this;
		}

		public IMultiCriteria Add(ICriteria criteria)
		{
			return Add<object>(criteria);
		}

		public IMultiCriteria Add(string key, ICriteria criteria)
		{
			return Add<object>(key, criteria);
		}

		public IMultiCriteria Add(DetachedCriteria detachedCriteria)
		{
			return Add<object>(detachedCriteria);
		}

		public IMultiCriteria Add(string key, DetachedCriteria detachedCriteria)
		{
			return Add<object>(key, detachedCriteria);
		}

		public IMultiCriteria Add<T>(ICriteria criteria)
		{
			criteriaQueries.Add(criteria);
			resultCollectionGenericType.Add(typeof(T));

			return this;
		}

		public IMultiCriteria Add<T>(string key, ICriteria criteria)
		{
			ThrowIfKeyAlreadyExists(key);
			criteriaQueries.Add(criteria);
			criteriaResultPositions.Add(key, criteriaQueries.Count - 1);
			resultCollectionGenericType.Add(typeof(T));

			return this;
		}

		public IMultiCriteria Add<T>(DetachedCriteria detachedCriteria)
		{
			criteriaQueries.Add(
				detachedCriteria.GetExecutableCriteria(session)
				);
			resultCollectionGenericType.Add(typeof(T));

			return this;
		}

		public IMultiCriteria Add<T>(string key, DetachedCriteria detachedCriteria)
		{
			ThrowIfKeyAlreadyExists(key);
			criteriaQueries.Add(detachedCriteria.GetExecutableCriteria(session));
			criteriaResultPositions.Add(key, criteriaQueries.Count - 1);
			resultCollectionGenericType.Add(typeof(T));

			return this;
		}

		public IMultiCriteria Add(System.Type resultGenericListType, IQueryOver queryOver)
		{
			return Add(resultGenericListType, queryOver.RootCriteria);
		}

		public IMultiCriteria Add<T>(IQueryOver<T> queryOver)
		{
			return Add<T>(queryOver.RootCriteria);
		}

		public IMultiCriteria Add<U>(IQueryOver queryOver)
		{
			return Add<U>(queryOver.RootCriteria);
		}

		public IMultiCriteria Add<T>(string key, IQueryOver<T> queryOver)
		{
			return Add<T>(key, queryOver.RootCriteria);
		}

		public IMultiCriteria Add<U>(string key, IQueryOver queryOver)
		{
			return Add<U>(key, queryOver.RootCriteria);
		}

		public IMultiCriteria SetCacheable(bool cachable)
		{
			isCacheable = cachable;
			return this;
		}

		public IMultiCriteria ForceCacheRefresh(bool forceRefresh)
		{
			forceCacheRefresh = forceRefresh;
			return this;
		}

		#region IMultiCriteria Members

		public IMultiCriteria SetResultTransformer(IResultTransformer resultTransformer)
		{
			this.resultTransformer = resultTransformer;
			return this;
		}

		public object GetResult(string key)
		{
			if (criteriaResults == null) List();

			if (!criteriaResultPositions.ContainsKey(key))
			{
				throw new InvalidOperationException(String.Format("The key '{0}' is unknown", key));
			}

			return criteriaResults[criteriaResultPositions[key]];
		}

		#endregion

		public IMultiCriteria SetCacheRegion(string cacheRegion)
		{
			this.cacheRegion = cacheRegion;
			return this;
		}

		private QueryParameters CreateCombinedQueryParameters()
		{
			QueryParameters combinedQueryParameters = new QueryParameters();
			combinedQueryParameters.ForceCacheRefresh = forceCacheRefresh;
			combinedQueryParameters.NamedParameters = new Dictionary<string, TypedValue>();
			ArrayList positionalParameterTypes = new ArrayList();
			ArrayList positionalParameterValues = new ArrayList();
			foreach (QueryParameters queryParameters in parameters)
			{
				// There aren't any named params in criteria queries
				//CopyNamedParametersDictionary(combinedQueryParameters.NamedParameters, queryParameters.NamedParameters);
				positionalParameterTypes.AddRange(queryParameters.PositionalParameterTypes);
				positionalParameterValues.AddRange(queryParameters.PositionalParameterValues);
			}
			combinedQueryParameters.PositionalParameterTypes = (IType[])positionalParameterTypes.ToArray(typeof(IType));
			combinedQueryParameters.PositionalParameterValues = (object[])positionalParameterValues.ToArray(typeof(object));
			return combinedQueryParameters;
		}

		private void ThrowIfKeyAlreadyExists(string key)
		{
			if (criteriaResultPositions.ContainsKey(key))
			{
				throw new InvalidOperationException(String.Format("The key '{0}' already exists", key));
			}
		}
	}
}
