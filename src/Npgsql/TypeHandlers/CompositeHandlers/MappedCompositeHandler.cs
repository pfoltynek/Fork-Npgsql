using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Npgsql.BackendMessages;
using Npgsql.PostgresTypes;
using Npgsql.TypeHandling;
using Npgsql.TypeMapping;
using NpgsqlTypes;

namespace Npgsql.TypeHandlers.CompositeHandlers
{
    class MappedCompositeHandler<T> : NpgsqlTypeHandler<T>, IMappedCompositeHandler
        where T : new()
    {
        static readonly Func<T> Constructor = Expression
            .Lambda<Func<T>>(Expression.New(typeof(T)))
            .Compile();

        readonly CompositeMemberHandler<T>[] _members;

        public Type CompositeType => typeof(T);

        MappedCompositeHandler(PostgresCompositeType postgresType, CompositeMemberHandler<T>[] members)
        {
            PostgresType = postgresType;
            _members = members;
        }

        public override async ValueTask<T> Read(NpgsqlReadBuffer buffer, int length, bool async, FieldDescription? fieldDescription = null)
        {
            await buffer.Ensure(sizeof(int), async);

            var fieldCount = buffer.ReadInt32();
            if (fieldCount != _members.Length)
                throw new InvalidOperationException($"pg_attributes contains {_members.Length} fields for type {PgDisplayName}, but {fieldCount} fields were received.");

            if (IsValueType<T>.Value)
            {
                var composite = new ByReference<T> { Value = Constructor() };
                foreach (var member in _members)
                    await member.Read(composite, buffer, async);

                return composite.Value;
            }
            else
            {
                var composite = Constructor();
                foreach (var member in _members)
                    await member.Read(composite, buffer, async);

                return composite;
            }
        }

        public override async Task Write(T value, NpgsqlWriteBuffer buffer, NpgsqlLengthCache? lengthCache, NpgsqlParameter? parameter, bool async)
        {
            if (buffer.WriteSpaceLeft < sizeof(int))
                await buffer.Flush(async);

            buffer.WriteInt32(_members.Length);

            foreach (var member in _members)
                await member.Write(value, buffer, lengthCache, async);
        }

        public override int ValidateAndGetLength(T value, ref NpgsqlLengthCache? lengthCache, NpgsqlParameter? parameter)
        {
            if (lengthCache == null)
                lengthCache = new NpgsqlLengthCache(1);

            if (lengthCache.IsPopulated)
                return lengthCache.Get();

            // Leave empty slot for the entire composite type, and go ahead an populate the element slots
            var position = lengthCache.Position;
            lengthCache.Set(0);

            // number of fields + (type oid + field length) * member count
            var length = sizeof(int) + sizeof(int) * 2 * _members.Length;
            foreach (var member in _members)
                length += member.ValidateAndGetLength(value, ref lengthCache);

            return lengthCache.Lengths[position] = length;
        }

        public static MappedCompositeHandler<T> Create(PostgresCompositeType pgType, ConnectorTypeMapper typeMapper, INpgsqlNameTranslator nameTranslator)
        {
            var clrType = typeof(T);
            var pgFields = pgType.Fields;
            var clrHandlers = new CompositeMemberHandler<T>[pgFields.Count];
            var clrHandlerCount = 0;

            var clrHandlerType = IsValueType<T>.Value
                ? typeof(CompositeStructMemberHandler<,>)
                : typeof(CompositeClassMemberHandler<,>);

            foreach (var clrMember in clrType.GetMembers(BindingFlags.Instance | BindingFlags.Public))
            {
                Type clrMemberType;
                switch (clrMember)
                {
                    case FieldInfo clrField:
                        clrMemberType = clrField.FieldType;
                        break;
                    case PropertyInfo clrProperty:
                        clrMemberType = clrProperty.PropertyType;
                        break;
                    default:
                        continue;
                }

                var attr = clrMember.GetCustomAttribute<PgNameAttribute>();
                var name = attr?.PgName ?? nameTranslator.TranslateMemberName(clrMember.Name);

                int pgFieldIndex;
                PostgresCompositeType.Field? pgField = null;

                for (pgFieldIndex = pgFields.Count - 1; pgFieldIndex >= 0; --pgFieldIndex)
                {
                    pgField = pgFields[pgFieldIndex];
                    if (pgField.Name == name) break;
                }

                if (pgField == null)
                    continue;

                if (clrHandlers[pgFieldIndex] != null)
                    throw new AmbiguousMatchException($"Multiple class members are mapped to the '{pgField.Name}' field.");

                if (!typeMapper.TryGetByOID(pgField.Type.OID, out var handler))
                    throw new Exception($"PostgreSQL composite type {pgType.DisplayName} has field {pgField.Type.DisplayName} with an unknown type (OID = {pgField.Type.OID}).");

                clrHandlerCount++;
                clrHandlers[pgFieldIndex] = (CompositeMemberHandler<T>)Activator.CreateInstance(
                    clrHandlerType.MakeGenericType(clrType, clrMemberType),
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    args: new object[] { clrMember, pgField.Type, handler },
                    culture: null);
            }

            if (clrHandlerCount == pgFields.Count)
                return new MappedCompositeHandler<T>(pgType, clrHandlers);

            var notMappedFields = string.Join(", ", clrHandlers
                .Select((member, memberIndex) => member == null ? $"'{pgFields[memberIndex].Name}'" : null)
                .Where(member => member != null));
            throw new InvalidOperationException($"PostgreSQL composite type {pgType.DisplayName} contains fields {notMappedFields} which could not match any on CLR type {clrType.Name}");
        }
    }
}
