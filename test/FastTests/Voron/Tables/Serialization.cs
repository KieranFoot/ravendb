﻿using System.Linq;
using Sparrow;
using Voron;
using Voron.Data.Tables;
using Xunit;
using Xunit.Sdk;

namespace FastTests.Voron.Tables
{
    public unsafe class Serialization : StorageTest
    {
        private void SchemaIndexDefEqual(TableSchema.SchemaIndexDef expectedIndex,
            TableSchema.SchemaIndexDef actualIndex)
        {
            if (expectedIndex == null)
            {
                Assert.Equal(null, actualIndex);
            }
            else
            {
                Assert.Equal(expectedIndex.IsGlobal, actualIndex.IsGlobal);
                Assert.Equal(expectedIndex.Count, actualIndex.Count);
                Assert.Equal(expectedIndex.Name, actualIndex.Name);
                Assert.True(SliceComparer.Equals(expectedIndex.NameAsSlice, actualIndex.NameAsSlice));
                Assert.Equal(expectedIndex.StartIndex, actualIndex.StartIndex);
                Assert.Equal(expectedIndex.Type, actualIndex.Type);
            }
        }

        private void FixedSchemaIndexDefEqual(TableSchema.FixedSizeSchemaIndexDef expectedIndex,
            TableSchema.FixedSizeSchemaIndexDef actualIndex)
        {
            if (expectedIndex == null)
            {
                Assert.Equal(null, actualIndex);
            }
            else
            {
                Assert.Equal(expectedIndex.IsGlobal, actualIndex.IsGlobal);
                Assert.Equal(expectedIndex.Name, actualIndex.Name);
                Assert.True(SliceComparer.Equals(expectedIndex.NameAsSlice, actualIndex.NameAsSlice));
                Assert.Equal(expectedIndex.StartIndex, actualIndex.StartIndex);
            }
        }

        private void SchemaDefEqual(TableSchema expected, TableSchema actual)
        {
            // Same primary keys
            SchemaIndexDefEqual(expected.Key, actual.Key);
            // Same keys for variable size indexes
            Assert.Equal(expected.Indexes.Keys.Count, actual.Indexes.Keys.Count);
            Assert.Equal(expected.Indexes.Keys.Count, expected.Indexes.Keys.Intersect(actual.Indexes.Keys).Count());
            // Same keys for fixed size indexes
            Assert.Equal(expected.FixedSizeIndexes.Keys.Count, actual.FixedSizeIndexes.Keys.Count);
            Assert.Equal(expected.FixedSizeIndexes.Keys.Count, expected.FixedSizeIndexes.Keys.Intersect(actual.FixedSizeIndexes.Keys).Count());
            // Same indexes
            foreach (var entry in expected.Indexes)
            {
                var other = actual.Indexes[entry.Key];
                SchemaIndexDefEqual(entry.Value, other);
            }

            foreach (var entry in expected.FixedSizeIndexes)
            {
                var other = actual.FixedSizeIndexes[entry.Key];
                FixedSchemaIndexDefEqual(entry.Value, other);
            }
        }

        [Fact]
        public void CanSerializeNormalIndex()
        {
            using (var tx = Env.WriteTransaction())
            {
                var expectedIndex = new TableSchema.SchemaIndexDef
                {
                    StartIndex = 2,
                    Count = 1,
                    Name = "Test Name",
                    NameAsSlice = Slice.From(StorageEnvironment.LabelsContext, "Test Name", ByteStringType.Immutable)
                };

                byte[] serialized = expectedIndex.Serialize();

                fixed (byte* serializedPtr = serialized)
                {
                    var actualIndex = TableSchema.SchemaIndexDef.ReadFrom(tx.Allocator, serializedPtr, serialized.Length);
                    Assert.Equal(serialized, actualIndex.Serialize());
                    SchemaIndexDefEqual(expectedIndex, actualIndex);
                }
            }
        }

        [Fact]
        public void CanSerializeNormalIndexWithoutName()
        {
            using (var tx = Env.WriteTransaction())
            {
                var expectedIndex = new TableSchema.SchemaIndexDef
                {
                    StartIndex = 3,
                    Count = 5,
                    NameAsSlice = Slice.From(StorageEnvironment.LabelsContext, "Test Name", ByteStringType.Immutable)
                };

                byte[] serialized = expectedIndex.Serialize();

                fixed (byte* serializedPtr = serialized)
                {
                    var actualIndex = TableSchema.SchemaIndexDef.ReadFrom(tx.Allocator, serializedPtr, serialized.Length);
                    Assert.Equal(serialized, actualIndex.Serialize());
                    SchemaIndexDefEqual(expectedIndex, actualIndex);
                }
            }
        }

        [Fact]
        public void CanSerializeFixedIndex()
        {
            using (var tx = Env.WriteTransaction())
            {
                var expectedIndex = new TableSchema.FixedSizeSchemaIndexDef()
                {
                    StartIndex = 2,
                    IsGlobal = true,
                    Name = "Test Name 2",
                    NameAsSlice = Slice.From(StorageEnvironment.LabelsContext, "Test Name 2", ByteStringType.Immutable)
                };

                byte[] serialized = expectedIndex.Serialize();

                fixed (byte* serializedPtr = serialized)
                {
                    var actualIndex = TableSchema.FixedSizeSchemaIndexDef.ReadFrom(tx.Allocator, serializedPtr, serialized.Length);
                    Assert.Equal(serialized, actualIndex.Serialize());
                    FixedSchemaIndexDefEqual(expectedIndex, actualIndex);
                }
            }
        }

        [Fact]
        public void CanSerializeFixedIndexWithoutName()
        {
            using (var tx = Env.WriteTransaction())
            {
                var expectedIndex = new TableSchema.FixedSizeSchemaIndexDef()
                {
                    StartIndex = 2,
                    IsGlobal = false,
                    NameAsSlice = Slice.From(StorageEnvironment.LabelsContext, "Test Name 2", ByteStringType.Immutable)
                };

                byte[] serialized = expectedIndex.Serialize();

                fixed (byte* serializedPtr = serialized)
                {
                    var actualIndex = TableSchema.FixedSizeSchemaIndexDef.ReadFrom(tx.Allocator, serializedPtr, serialized.Length);
                    Assert.Equal(serialized, actualIndex.Serialize());
                    FixedSchemaIndexDefEqual(expectedIndex, actualIndex);
                }
            }
        }

        [Fact]
        public void CanSerializeSchema()
        {
            using (var tx = Env.WriteTransaction())
            {
                var tableSchema = new TableSchema()
                    .DefineIndex("Index 1", new TableSchema.SchemaIndexDef
                    {
                        StartIndex = 2,
                        Count = 1,
                    })
                    .DefineFixedSizeIndex("Index 2", new TableSchema.FixedSizeSchemaIndexDef()
                    {
                        StartIndex = 2,
                        IsGlobal = true
                    })
                    .DefineKey(new TableSchema.SchemaIndexDef
                    {
                        StartIndex = 3,
                        Count = 1,
                    });

                byte[] serialized = tableSchema.SerializeSchema();

                fixed (byte* ptr = serialized)
                {
                    var actualTableSchema = TableSchema.ReadFrom(tx.Allocator, ptr, serialized.Length);
                    // This checks that reserializing is the same
                    Assert.Equal(serialized, actualTableSchema.SerializeSchema());
                    // This checks that what was deserialized is correct
                    SchemaDefEqual(tableSchema, actualTableSchema);
                }
            }
        }
        
        [Fact]
        public void CanSerializeMultiIndexSchema()
        {
            using (var tx = Env.WriteTransaction())
            {
                var tableSchema = new TableSchema()
                    .DefineIndex("Index 1", new TableSchema.SchemaIndexDef
                    {
                        StartIndex = 2,
                        Count = 1,
                    })
                    .DefineIndex("Index 2", new TableSchema.SchemaIndexDef
                    {
                        StartIndex = 1,
                        Count = 1,
                    })
                    .DefineFixedSizeIndex("Index 3", new TableSchema.FixedSizeSchemaIndexDef()
                    {
                        StartIndex = 2,
                        IsGlobal = true
                    })
                    .DefineKey(new TableSchema.SchemaIndexDef
                    {
                        StartIndex = 3,
                        Count = 1,
                    });

                byte[] serialized = tableSchema.SerializeSchema();

                fixed (byte* ptr = serialized)
                {
                    var actualTableSchema = TableSchema.ReadFrom(tx.Allocator, ptr, serialized.Length);
                    // This checks that reserializing is the same
                    Assert.Equal(serialized, actualTableSchema.SerializeSchema());
                    // This checks that what was deserialized is correct
                    SchemaDefEqual(tableSchema, actualTableSchema);
                }
            }
        }
        
    }
}

