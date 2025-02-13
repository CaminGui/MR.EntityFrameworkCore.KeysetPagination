﻿using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MR.EntityFrameworkCore.KeysetPagination.Columns;

namespace MR.EntityFrameworkCore.KeysetPagination;

public static class KeysetPaginationExtensions
{
	private static readonly IReadOnlyDictionary<Type, MethodInfo> TypeToCompareToMethod = new Dictionary<Type, MethodInfo>
	{
		{ typeof(string), GetCompareToMethod(typeof(string)) },
		{ typeof(Guid), GetCompareToMethod(typeof(Guid)) },
		{ typeof(bool), GetCompareToMethod(typeof(bool)) },
	};

	private static readonly ConstantExpression ConstantExpression0 = Expression.Constant(0);

	private static KeysetPaginationContext<T> InternalKeysetPaginate<T>(
		this IQueryable<T> source,
		Action<KeysetPaginationBuilder<T>> builderAction,
		KeysetPaginationDirection direction = KeysetPaginationDirection.Forward,
		object? reference = null,
		bool hasOrderBy = false)
		where T : class
	{
		if (source == null) throw new ArgumentNullException(nameof(source));
		if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));

		var builder = new KeysetPaginationBuilder<T>();
		builderAction(builder);
		var columns = builder.Columns;

		if (!columns.Any())
		{
			throw new InvalidOperationException("There should be at least one configured column in the keyset.");
		}

		// Order
		IOrderedQueryable<T> orderedQuery;
		if (hasOrderBy)
		{
			orderedQuery = columns[0].ApplyThenOrderBy((IOrderedQueryable<T>)source, direction);
		}
		else
		{
			orderedQuery = columns[0].ApplyOrderBy(source, direction);
		}
		for (var i = 1; i < columns.Count; i++)
		{
			orderedQuery = columns[i].ApplyThenOrderBy(orderedQuery, direction);
		}

		// Filter
		var filteredQuery = orderedQuery.AsQueryable();
		if (reference != null)
		{
			var keysetFilterPredicateLambda = BuildKeysetFilterPredicateExpression(columns, direction, reference);
			filteredQuery = filteredQuery.Where(keysetFilterPredicateLambda);
		}

		return new KeysetPaginationContext<T>(filteredQuery, orderedQuery, columns, direction, reference != null);
	}

		/// <summary>
		/// Paginates using keyset pagination.
		/// </summary>
		/// <typeparam name="T">The type of the entity.</typeparam>
		/// <param name="source">An <see cref="IQueryable{T}"/> to paginate.</param>
		/// <param name="builderAction">An action that takes a builder and registers the columns upon which keyset pagination will work.</param>
		/// <param name="direction">The direction to take. Default is Forward.</param>
		/// <param name="reference">The reference object. Needs to have properties with exact names matching the configured properties. Doesn't necessarily need to be the same type as T.</param>
		/// <returns>An object containing the modified queryable. Can be used with other helper methods related to keyset pagination.</returns>
		/// <exception cref="ArgumentNullException">source or builderAction is null.</exception>
		/// <exception cref="InvalidOperationException">If no properties were registered with the builder.</exception>
		/// <remarks>
		/// Note that calling this method will override any OrderBy calls you have done before.
		/// </remarks>
	public static KeysetPaginationContext<T> KeysetPaginate<T>(
		this IQueryable<T> source,
		Action<KeysetPaginationBuilder<T>> builderAction,
		KeysetPaginationDirection direction = KeysetPaginationDirection.Forward,
		object? reference = null)
		where T : class
	{
		return InternalKeysetPaginate(source, builderAction, direction, reference);
	}

	public static KeysetPaginationContext<T> KeysetPaginate<T>(
		this IOrderedQueryable<T> source,
		Action<KeysetPaginationBuilder<T>> builderAction,
		KeysetPaginationDirection direction = KeysetPaginationDirection.Forward,
		object? reference = null)
		where T : class
	{
		return InternalKeysetPaginate(source, builderAction, direction, reference, true);
	}

	/// <summary>
	/// Paginates using keyset pagination.
	/// </summary>
	/// <typeparam name="T">The type of the entity.</typeparam>
	/// <param name="source">An <see cref="IQueryable{T}"/> to paginate.</param>
	/// <param name="builderAction">An action that takes a builder and registers the columns upon which keyset pagination will work.</param>
	/// <param name="direction">The direction to take. Default is Forward.</param>
	/// <param name="reference">The reference object. Needs to have properties with exact names matching the configured properties. Doesn't necessarily need to be the same type as T.</param>
	/// <returns>The modified the queryable.</returns>
	/// <exception cref="ArgumentNullException">source or builderAction is null.</exception>
	/// <exception cref="InvalidOperationException">If no properties were registered with the builder.</exception>
	/// <remarks>
	/// Note that calling this method will override any OrderBy calls you have done before.
	/// </remarks>
	public static IQueryable<T> KeysetPaginateQuery<T>(
		this IQueryable<T> source,
		Action<KeysetPaginationBuilder<T>> builderAction,
		KeysetPaginationDirection direction = KeysetPaginationDirection.Forward,
		object? reference = null)
		where T : class
	{
		return KeysetPaginate(source, builderAction, direction, reference).Query;
	}
	public static IQueryable<T> KeysetPaginateQuery<T>(
		this IOrderedQueryable<T> source,
		Action<KeysetPaginationBuilder<T>> builderAction,
		KeysetPaginationDirection direction = KeysetPaginationDirection.Forward,
		object? reference = null)
		where T : class
	{
		return KeysetPaginate(source, builderAction, direction, reference).Query;
	}

	/// <summary>
	/// Returns true when there is more data before the list.
	/// </summary>
	/// <typeparam name="T">The type of the entity.</typeparam>
	/// <typeparam name="T2">The type of the elements of the data.</typeparam>
	/// <param name="context">The <see cref="KeysetPaginationContext{T}"/> object.</param>
	/// <param name="data">The data list.</param>
	/// <param name="areEqualItems">The items comparer.</param>
	public static Task<bool> HasPreviousAsync<T, T2>(
		this KeysetPaginationContext<T> context,
		IReadOnlyList<T2> data,
		Func<T, T2, bool> areEqualItems)
		where T : class
	{
		if (data == null) throw new ArgumentNullException(nameof(data));
		if (context == null) throw new ArgumentNullException(nameof(context));

		if (!data.Any())
		{
			return Task.FromResult(false);
		}

		if (context.Direction == KeysetPaginationDirection.Forward)
		{
			if (context.HasInitialReference)
			{
				return Task.FromResult(true);
			}
			else
			{
				return Task.FromResult(false);
			}
		}

		// Get first items and see if they match.
		var firstItemEntireList = context.OrderedQuery.LastOrDefault();
		var firstItemFetchedList = data.FirstOrDefault();
		if (null == firstItemEntireList ||
			null == firstItemFetchedList)
		{
			return Task.FromResult(false);
		}

		return Task.FromResult(!areEqualItems(firstItemEntireList, firstItemFetchedList));
	}

	/// <summary>
	/// Returns true when there is more data after the list.
	/// </summary>
	/// <typeparam name="T">The type of the entity.</typeparam>
	/// <typeparam name="T2">The type of the elements of the data.</typeparam>
	/// <param name="context">The <see cref="KeysetPaginationContext{T}"/> object.</param>
	/// <param name="data">The data list.</param>
	/// <param name="areEqualItems">The items comparer.</param>
	public static Task<bool> HasNextAsync<T, T2>(
		this KeysetPaginationContext<T> context,
		IReadOnlyList<T2> data,
		Func<T, T2, bool> areEqualItems)
		where T : class
	{
		if (data == null) throw new ArgumentNullException(nameof(data));
		if (context == null) throw new ArgumentNullException(nameof(context));

		if (!data.Any())
		{
			return Task.FromResult(false);
		}

		if (context.Direction == KeysetPaginationDirection.Backward)
		{
			if (context.HasInitialReference)
			{
				return Task.FromResult(true);
			}
			else
			{
				return Task.FromResult(false);
			}
		}

		// Get last items and see if they match.
		var lastItemEntireList = context.OrderedQuery.LastOrDefault();
		var lastItemFetchedList = data.LastOrDefault();
		if (null == lastItemEntireList ||
			null == lastItemFetchedList)
		{
			return Task.FromResult(false);
		}

		return Task.FromResult(!areEqualItems(lastItemEntireList, lastItemFetchedList));
	}

	/// <summary>
	/// Returns true when there is more data before the list.
	/// </summary>
	/// <typeparam name="T">The type of the entity.</typeparam>
	/// <typeparam name="T2">The type of the elements of the data.</typeparam>
	/// <param name="context">The <see cref="KeysetPaginationContext{T}"/> object.</param>
	/// <param name="data">The data list.</param>
	public static Task<bool> HasPreviousAsync<T, T2>(
		this KeysetPaginationContext<T> context,
		IReadOnlyList<T2> data)
		where T : class
	{
		if (data == null) throw new ArgumentNullException(nameof(data));
		if (context == null) throw new ArgumentNullException(nameof(context));

		if (!data.Any())
		{
			return Task.FromResult(false);
		}

		// Get first item and see if there's anything before it.
		var reference = data[0]!;
		return HasAsync(context, KeysetPaginationDirection.Backward, reference);
	}

	/// <summary>
	/// Returns true when there is more data after the list.
	/// </summary>
	/// <typeparam name="T">The type of the entity.</typeparam>
	/// <typeparam name="T2">The type of the elements of the data.</typeparam>
	/// <param name="context">The <see cref="KeysetPaginationContext{T}"/> object.</param>
	/// <param name="data">The data list.</param>
	public static Task<bool> HasNextAsync<T, T2>(
		this KeysetPaginationContext<T> context,
		IReadOnlyList<T2> data)
		where T : class
	{
		if (data == null) throw new ArgumentNullException(nameof(data));
		if (context == null) throw new ArgumentNullException(nameof(context));

		if (!data.Any())
		{
			return Task.FromResult(false);
		}

		// Get last item and see if there's anything after it.
		var reference = data[^1]!;
		return HasAsync(context, KeysetPaginationDirection.Forward, reference);
	}

	private static Task<bool> HasAsync<T>(
		this KeysetPaginationContext<T> context,
		KeysetPaginationDirection direction,
		object reference)
		where T : class
	{
		var lambda = BuildKeysetFilterPredicateExpression(
			context.Columns, direction, reference);
		return context.OrderedQuery.AnyAsync(lambda);
	}

	private static List<object> GetValues<T>(
		IReadOnlyList<IKeysetColumn<T>> columns,
		object reference)
		where T : class
	{
		var referenceValues = new List<object>(capacity: columns.Count);
		foreach (var column in columns)
		{
			var value = column.ObtainValue(reference);
			referenceValues.Add(value);
		}
		return referenceValues;
	}

	/// <summary>
	/// Ensures the data list is correctly ordered.
	/// Basically applies a reverse on the data if the KeysetPaginate direction was Backward.
	/// </summary>
	/// <typeparam name="T">The type of the entity.</typeparam>
	/// <typeparam name="T2">The type of the elements of the data.</typeparam>
	/// <param name="context">The <see cref="KeysetPaginationContext{T}"/> object.</param>
	/// <param name="data">The data list.</param>
	public static void EnsureCorrectOrder<T, T2>(
		this KeysetPaginationContext<T> context,
		List<T2> data)
		where T : class
	{
		if (data == null) throw new ArgumentNullException(nameof(data));
		if (context == null) throw new ArgumentNullException(nameof(context));

		if (context.Direction == KeysetPaginationDirection.Backward)
		{
			data.Reverse();
		}
	}

	private static Expression<Func<T, bool>> BuildKeysetFilterPredicateExpression<T>(
		IReadOnlyList<IKeysetColumn<T>> columns,
		KeysetPaginationDirection direction,
		object reference)
		where T : class
	{
		// A composite keyset pagination in sql looks something like this:
		//   (x, y, ...) > (a, b, ...)
		// Where, x/y/... represent the column and a/b/... represent the reference's respective values.
		//
		// In sql standard this syntax is called "row value". Check here: https://use-the-index-luke.com/sql/partial-results/fetch-next-page#sb-row-values
		// Unfortunately, not all databases support this properly.
		// Further, if we were to use this we would somehow need EF Core to recognise it and translate it
		// perhaps by using a new DbFunction (https://docs.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbfunctions).
		// There's an ongoing issue for this here: https://github.com/dotnet/efcore/issues/26822
		//
		// In addition, row value won't work for mixed ordered columns. i.e if x > a but y < b.
		// So even if we can use it we'll still have to fallback to this logic in these cases.
		//
		// The generalized expression for this in pseudocode is:
		//   (x > a) OR
		//   (x = a AND y > b) OR
		//   (x = a AND y = b AND z > c) OR...
		//
		// Of course, this will be a bit more complex when ASC and DESC are mixed.
		// Assume x is ASC, y is DESC, and z is ASC:
		//   (x > a) OR
		//   (x = a AND y < b) OR
		//   (x = a AND y = b AND z > c) OR...
		//
		// An optimization is to include an additional redundant wrapping clause for the 1st column when there are
		// more than one column we're acting on, which would allow the db to use it as an access predicate on the 1st column.
		// See here: https://use-the-index-luke.com/sql/partial-results/fetch-next-page#sb-equivalent-logic

		var referenceValues = GetValues(columns, reference);

		var firstMemberAccessExpression = default(Expression);
		var firstReferenceValueExpression = default(Expression);
		object? firstReferenceValue = default;

		// entity =>
		var param = Expression.Parameter(typeof(T), "entity");

		var orExpression = default(BinaryExpression)!;
		var innerLimit = 1;
		// This loop compounds the outer OR expressions.
		for (var i = 0; i < columns.Count; i++)
		{
			var andExpression = default(BinaryExpression)!;

			// This loop compounds the inner AND expressions.
			// innerLimit implicitly grows from 1 to items.Count by each iteration.
			for (var j = 0; j < innerLimit; j++)
			{
				var isInnerLastOperation = j + 1 == innerLimit;
				var column = columns[j];
				var memberAccess = column.MakeMemberAccessExpression(param);
				var referenceValue = referenceValues[j];
				Expression<Func<object>> referenceValueFunc = () => referenceValue;
				var referenceValueExpression = referenceValueFunc.Body;

				// Handle Enum access by casting it to underlying type and using it as constant expression
				if (memberAccess.Type.IsEnum)
				{
					var enumBaseType = Enum.GetUnderlyingType(memberAccess.Type);
					referenceValueExpression = Expression.Constant(Convert.ChangeType(referenceValue, enumBaseType));
					memberAccess = Expression.Convert(memberAccess, enumBaseType);
				}

				if (firstMemberAccessExpression == null)
				{
					// This might be used later on in an optimization.
					firstMemberAccessExpression = memberAccess;
					firstReferenceValueExpression = referenceValueExpression;
					firstReferenceValue = referenceValue;
				}

				BinaryExpression innerExpression;
				if (!isInnerLastOperation)
				{
					innerExpression = Expression.Equal(
						memberAccess,
						EnsureMatchingType(memberAccess, referenceValueExpression));
				}
				else
				{
					var compare = GetComparisonExpressionToApply(direction, column, referenceValue, orEqual: false);
					innerExpression = MakeComparisonExpression(
						column,
						memberAccess, referenceValueExpression,
						compare);
				}

				andExpression = andExpression == null ? innerExpression : Expression.And(andExpression, innerExpression);
			}

			orExpression = orExpression == null ? andExpression : Expression.Or(orExpression, andExpression);

			innerLimit++;
		}

		var finalExpression = orExpression;
		if (columns.Count > 1)
		{
			// Implement the optimization that allows an access predicate on the 1st column.
			// This is done by generating the following expression:
			//   (x >=|<= a) AND (previous generated expression)
			//
			// This effectively adds a redundant clause on the 1st column, but it's a clause all dbs
			// understand and can use as an access predicate (most commonly when the column is indexed).

			var firstColumn = columns[0];

			Expression? left = default;
			if (null == firstReferenceValue)
			{
				var compare1 = GetComparisonExpressionToApply(direction, firstColumn, firstReferenceValue, orEqual: false);
				var innerLeft = MakeComparisonExpression(
					firstColumn,
					firstMemberAccessExpression!, firstReferenceValueExpression!,
					compare1);

				var compare2 = GetComparisonExpressionToApply(direction, firstColumn, firstReferenceValue, orEqual: true);
				var innerRight = MakeComparisonExpression(
					firstColumn,
					firstMemberAccessExpression!, firstReferenceValueExpression!,
					compare2);

				left = Expression.Or(innerLeft, innerRight);
			}
			else
			{
				var compare = GetComparisonExpressionToApply(direction, firstColumn, firstReferenceValue, orEqual: true);
				left = MakeComparisonExpression(
					firstColumn,
					firstMemberAccessExpression!, firstReferenceValueExpression!,
					compare);
			}

			finalExpression = Expression.And(left, finalExpression);
		}

		return Expression.Lambda<Func<T, bool>>(finalExpression, param);
	}

	private static BinaryExpression MakeComparisonExpression<T>(
		IKeysetColumn<T> column,
		Expression memberAccess, Expression referenceValue,
		Func<Expression, Expression, BinaryExpression> compare)
		where T : class
	{
		var propertyType = GetMemberExpressionType(memberAccess);
		var pt = propertyType.IsEnum ? typeof(Enum) : propertyType;
		if (TypeToCompareToMethod.TryGetValue(pt, out var compareToMethod))
		{
			// LessThan/GreaterThan operators are not valid for some types such as strings and guids.
			// We use the CompareTo method on these types instead.

			// entity.Property.CompareTo(referenceValue) >|< 0
			// -----------------------------------------

			// entity.Property.CompareTo(referenceValue)
			var methodCallExpression = Expression.Call(
				memberAccess,
				compareToMethod,
				EnsureMatchingType(memberAccess, referenceValue));

			// >|< 0
			return compare(methodCallExpression, ConstantExpression0);
		}
		else
		{
			return compare(
				memberAccess,
				EnsureMatchingType(memberAccess, referenceValue));
		}
	}

	private static Type GetMemberExpressionType(Expression e)
	{
		MemberExpression? member;
		if ((member = e as MemberExpression) != null)
		{
			return member.Type;
		}

		UnaryExpression? unary;
		if ((unary = e as UnaryExpression) != null)
		{
			if ((member = unary.Operand as MemberExpression) != null)
			{
				return member.Type;
			}
		}

		MethodCallExpression? call;
		if ((call = e as MethodCallExpression) != null)
		{
			return call.Type;
		}

		throw new InvalidOperationException("Cannot get MemberExpression Type from input Expression");
	}

	private static Expression EnsureMatchingType(
		Expression memberExpression,
		Expression targetExpression)
	{
		var memberType = GetMemberExpressionType(memberExpression);

		// If the target has a different type we should convert it.
		// Originally this happened with nullables only, but now that we use expressions
		// for the target access instead of constants we'll need this or else comparison won't work
		// between unmatching types (i.e int (member) compared to object (target)).
		if (memberType != targetExpression.Type)
		{
			if (memberType.IsEnum)
			{
				return targetExpression;
			}

			return Expression.Convert(targetExpression, memberType);
		}

		return targetExpression;
	}

	private static Func<Expression, Expression, BinaryExpression> GetComparisonExpressionToApply<T>(
		KeysetPaginationDirection direction, IKeysetColumn<T> column, object? referenceValue, bool orEqual)
		where T : class
	{
		if (null == referenceValue)
		{
			if (!orEqual)
			{
				return Expression.NotEqual;
			}
			else
			{
				return Expression.Equal;
			}
		}

		var greaterThan = direction switch
		{
			KeysetPaginationDirection.Forward when !column.IsDescending => true,
			KeysetPaginationDirection.Forward when column.IsDescending => false,
			KeysetPaginationDirection.Backward when !column.IsDescending => false,
			KeysetPaginationDirection.Backward when column.IsDescending => true,
			_ => throw new NotImplementedException(),
		};

		return orEqual ?
			(greaterThan ? Expression.GreaterThanOrEqual : Expression.LessThanOrEqual) :
			(greaterThan ? Expression.GreaterThan : Expression.LessThan);
	}

	private static MethodInfo GetCompareToMethod(Type type)
	{
		var methodInfo = type.GetTypeInfo().GetMethod(nameof(string.CompareTo), new Type[] { type });
		if (methodInfo == null)
		{
			throw new InvalidOperationException($"Didn't find a CompareTo method on type {type.Name}.");
		}

		return methodInfo;
	}
}
