﻿using System;
using System.Linq;
using System.Reflection;
using NHibernate.Cfg;
using NHibernate.Cfg.MappingSchema;
using NHibernate.Collection;
using NHibernate.Engine.Query;
using NHibernate.Mapping.ByCode;
using NHibernate.Util;
using NUnit.Framework;

namespace NHibernate.Test.NHSpecificTest.NH2319
{
	[TestFixture]
	public class MapFixture : TestCaseMappingByCode
	{
		private Guid _parent1Id;
		private Guid _child1Id;
		private Guid _parent2Id;
		private Guid _child3Id;

		[Test]
		public void ShouldBeAbleToFindChildrenByName()
		{
			FindChildrenByName(_parent1Id, _child1Id);
		}

		private void FindChildrenByName(Guid parentId, Guid childId)
		{
			using (var session = OpenSession())
			using (session.BeginTransaction())
			{
				var parent = session.Get<Parent>(parentId);

				Assert.That(parent, Is.Not.Null);

				var filtered = parent.ChildrenMap.Values
					.AsQueryable()
					.Where(x => x.Name == "Jack")
					.ToList();

				Assert.That(filtered, Has.Count.EqualTo(1));
				Assert.That(filtered[0].Id, Is.EqualTo(childId));
			}
		}

		[Test]
		public void ShouldBeAbleToPerformComplexFiltering()
		{
			using (var session = OpenSession())
			using (session.BeginTransaction())
			{
				var parent = session.Get<Parent>(_parent1Id);

				Assert.NotNull(parent);

				var filtered = parent.ChildrenMap.Values
					.AsQueryable()
					.Where(x => x.Name == "Piter")
					.SelectMany(x => x.GrandChildren)
					.Select(x => x.Id)
					.Count();

				Assert.That(filtered, Is.EqualTo(2));
			}
		}

		[Test]
		public void ShouldBeAbleToReuseQueryPlan()
		{
			ShouldBeAbleToFindChildrenByName();
			using (var spy = new LogSpy(typeof(QueryPlanCache)))
			{
				Assert.That(ShouldBeAbleToFindChildrenByName, Throws.Nothing);
				AssertFilterPlanCacheHit(spy);
			}
		}

		[Test]
		public void ShouldNotMixResults()
		{
			FindChildrenByName(_parent1Id, _child1Id);
			using (var spy = new LogSpy(typeof(QueryPlanCache)))
			{
				FindChildrenByName(_parent2Id, _child3Id);
				AssertFilterPlanCacheHit(spy);
			}
		}

		[Test]
		public void ShouldNotInitializeCollectionWhenPerformingQuery()
		{
			using (var session = OpenSession())
			using (session.BeginTransaction())
			{
				var parent = session.Get<Parent>(_parent1Id);
				Assert.That(parent, Is.Not.Null);

				var persistentCollection = (IPersistentCollection) parent.ChildrenMap;

				var filtered = parent.ChildrenMap.Values
					.AsQueryable()
					.Where(x => x.Name == "Jack")
					.ToList();

				Assert.That(filtered, Has.Count.EqualTo(1));
				Assert.That(persistentCollection.WasInitialized, Is.False);
			}
		}

		[Test]
		public void ShouldPerformSqlQueryEvenIfCollectionAlreadyInitialized()
		{
			using (var session = OpenSession())
			using (session.BeginTransaction())
			{
				var parent = session.Get<Parent>(_parent1Id);
				Assert.That(parent, Is.Not.Null);

				var loaded = parent.ChildrenMap.ToList();
				Assert.That(loaded, Has.Count.EqualTo(2));

				var countBeforeFiltering = session.SessionFactory.Statistics.QueryExecutionCount;

				var filtered = parent.ChildrenMap.Values
					.AsQueryable()
					.Where(x => x.Name == "Jack")
					.ToList();

				var countAfterFiltering = session.SessionFactory.Statistics.QueryExecutionCount;

				Assert.That(filtered, Has.Count.EqualTo(1));
				Assert.That(countAfterFiltering, Is.EqualTo(countBeforeFiltering + 1));
			}
		}

		[Test]
		public void TestFilter()
		{
			using (var session = OpenSession())
			using (session.BeginTransaction())
			{
				var parent = session.Get<Parent>(_parent1Id);
				Assert.That(parent, Is.Not.Null);

				var children = session.CreateFilter(parent.ChildrenMap, "where this.Name = 'Jack'")
					.List<Child>();

				Assert.That(children, Has.Count.EqualTo(1));
			}
		}

		[Test]
		public void TestPlanCacheMiss()
		{
			var internalPlanCache = typeof(QueryPlanCache)
				.GetField("planCache", BindingFlags.NonPublic | BindingFlags.Instance)
				?.GetValue(Sfi.QueryPlanCache) as SoftLimitMRUCache;
			Assert.That(internalPlanCache, Is.Not.Null,
				$"Unable to find the internal query plan cache for clearing it, please adapt code to current {nameof(QueryPlanCache)} implementation.");

			using (var spy = new LogSpy(typeof(QueryPlanCache)))
			{
				internalPlanCache.Clear();
				ShouldBeAbleToFindChildrenByName();
				AssertFilterPlanCacheMiss(spy);
			}
		}

		private const string _filterPlanCacheMissLog = "unable to locate collection-filter query plan in cache";

		private static void AssertFilterPlanCacheHit(LogSpy spy) =>
			// Each query currently ask the cache two times, so asserting reuse requires to check cache has not been missed
			// rather than only asserting it has been hit.
			Assert.That(spy.GetWholeLog(),
				Contains.Substring("located collection-filter query plan in cache (")
				.And.Not.Contains(_filterPlanCacheMissLog));

		private static void AssertFilterPlanCacheMiss(LogSpy spy) =>
			Assert.That(spy.GetWholeLog(), Contains.Substring(_filterPlanCacheMissLog));

		protected override void Configure(Configuration configuration)
		{
			configuration.SetProperty("show_sql", "true");
			configuration.SetProperty("generate_statistics", "true");
		}

		protected override void OnSetUp()
		{
			using (var session = OpenSession())
			using (var transaction = session.BeginTransaction())
			{
				var parent1 = new Parent { Name = "Bob" };
				_parent1Id = (Guid) session.Save(parent1);

				var parent2 = new Parent { Name = "Martin" };
				_parent2Id = (Guid) session.Save(parent2);

				var child1 = new Child
				{
					Name = "Jack",
					Parent = parent1
				};
				_child1Id = (Guid) session.Save(child1);
				parent1.ChildrenMap.Add(child1.Id, child1);

				var child2 = new Child
				{
					Name = "Piter",
					Parent = parent1
				};
				session.Save(child2);
				parent1.ChildrenMap.Add(child2.Id, child2);

				var grandChild1 = new GrandChild
				{
					Name = "Kate",
					Child = child2
				};
				session.Save(grandChild1);
				child2.GrandChildren.Add(grandChild1);

				var grandChild2 = new GrandChild
				{
					Name = "Mary",
					Child = child2
				};
				session.Save(grandChild2);
				child2.GrandChildren.Add(grandChild2);

				var child3 = new Child
				{
					Name = "Jack",
					Parent = parent2
				};
				_child3Id = (Guid) session.Save(child3);
				parent2.ChildrenMap.Add(child1.Id, child3);

				session.Flush();
				transaction.Commit();
			}
		}

		protected override void OnTearDown()
		{
			using (var session = OpenSession())
			using (var transaction = session.BeginTransaction())
			{
				session.Delete("from System.Object");

				session.Flush();
				transaction.Commit();
			}
		}

		protected override HbmMapping GetMappings()
		{
			var mapper = new ModelMapper();
			mapper.Class<Parent>(rc =>
			{
				rc.Id(x => x.Id, m => m.Generator(Generators.GuidComb));
				rc.Property(x => x.Name);
				rc.Map(x => x.ChildrenMap,
					map => map.Cascade(Mapping.ByCode.Cascade.All | Mapping.ByCode.Cascade.DeleteOrphans),
					rel => rel.OneToMany());
			});

			mapper.Class<Child>(rc =>
			{
				rc.Id(x => x.Id, m => m.Generator(Generators.GuidComb));
				rc.Property(x => x.Name);
				rc.Set(x => x.GrandChildren,
					map => map.Cascade(Mapping.ByCode.Cascade.All | Mapping.ByCode.Cascade.DeleteOrphans),
					rel => rel.OneToMany());
			});

			mapper.Class<GrandChild>(rc =>
			{
				rc.Id(x => x.Id, m => m.Generator(Generators.GuidComb));
				rc.Property(x => x.Name);
			});

			return mapper.CompileMappingForAllExplicitlyAddedEntities();
		}
	}
}
