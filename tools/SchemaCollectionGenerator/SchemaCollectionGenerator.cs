using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SchemaCollectionGenerator.SchemaCollections.yaml");
using var reader = new StreamReader(stream!);

var deserializer = new DeserializerBuilder()
	.WithNamingConvention(CamelCaseNamingConvention.Instance)
	.Build();
var schemaCollections = deserializer.Deserialize<List<Schema>>(reader);

using var codeWriter = new StreamWriter(@"..\..\..\..\..\src\MySqlConnector\Core\SchemaProvider.g.cs");
codeWriter.Write(@"// DO NOT EDIT - generated by SchemaCollectionGenerator.cs
#nullable enable
using MySqlConnector.Protocol.Serialization;
using MySqlConnector.Utilities;

namespace MySqlConnector.Core;

internal sealed partial class SchemaProvider
{
	public async ValueTask<DataTable> GetSchemaAsync(IOBehavior ioBehavior, string collectionName, string?[]? restrictionValues, CancellationToken cancellationToken)
	{
		if (collectionName is null)
			throw new ArgumentNullException(nameof(collectionName));

		var dataTable = new DataTable(collectionName);
");
string elseIf = "if";
foreach (var schema in schemaCollections)
{
	codeWriter.Write(@$"		{elseIf} (string.Equals(collectionName, ""{schema.Name}"", StringComparison.OrdinalIgnoreCase))
			await Fill{schema.Name}Async(ioBehavior, dataTable, restrictionValues, cancellationToken).ConfigureAwait(false);
");
	elseIf = "else if";
}
codeWriter.Write(@"		else
			throw new ArgumentException(""Invalid collection name: '"" + collectionName + ""'."", nameof(collectionName));

		return dataTable;
	}

");

foreach (var schema in schemaCollections)
{
	var isAsync = schema.Table is not null;
	var supportsRestrictions = schema.Restrictions is { Count: > 0 };
	codeWriter.Write($@"	private {(isAsync ? "async " : "")}Task Fill{schema.Name}Async(IOBehavior ioBehavior, DataTable dataTable, string?[]? restrictionValues, CancellationToken cancellationToken)
	{{
");
	if (!supportsRestrictions)
	{
		codeWriter.Write($@"		if (restrictionValues is not null)
			throw new ArgumentException(""restrictionValues is not supported for schema '{schema.Name}'."", nameof(restrictionValues));
");
	}
	else
	{
		codeWriter.Write($@"		if (restrictionValues is {{ Length: > {schema.Restrictions!.Count} }})
			throw new ArgumentException(""More than {schema.Restrictions.Count} restrictionValues are not supported for schema '{schema.Name}'."", nameof(restrictionValues));
");
	}

	codeWriter.Write($@"
		dataTable.Columns.AddRange(new DataColumn[]
		{{
");
	foreach (var column in schema.Columns)
	{
		codeWriter.Write($@"			new(""{column.Name}"", typeof({column.Type})),
");
	}
	codeWriter.Write($@"		}});

");
	if (schema.Table is string table)
	{
		if (supportsRestrictions)
		{
			codeWriter.Write($@"		var columns = new List<KeyValuePair<string, string>>();
		if (restrictionValues is not null)
		{{
");
			for (var i = 0; i < schema.Restrictions!.Count; i++)
			{
				if (!schema.Columns.Any(x => x.Name == schema.Restrictions[i].Default))
					throw new InvalidOperationException("Restriction.Default must match a Column Name");
				codeWriter.Write($@"			if (restrictionValues.Length > {i} && !string.IsNullOrEmpty(restrictionValues[{i}]))
				columns.Add(new(""{schema.Restrictions[i].Default}"", restrictionValues[{i}]!));
");
			}
			codeWriter.Write(@"		}

");
		}

		codeWriter.Write(@$"		await FillDataTableAsync(ioBehavior, dataTable, ""{table}"", {(supportsRestrictions ? "columns," : "null,")} cancellationToken).ConfigureAwait(false);");
	}
	else if (schema.Name == "MetaDataCollections")
	{
		foreach (var schemaCollection in schemaCollections)
		{
			codeWriter.Write($@"		dataTable.Rows.Add(""{schemaCollection.Name}"", {schemaCollection.Restrictions?.Count ?? 0}, {schemaCollection.IdentifierPartCount});
");
		}
	}
	else if (schema.Name == "Restrictions")
	{
		foreach (var schemaCollection in schemaCollections)
		{
			if (schemaCollection.Restrictions is { Count: > 0 })
			{
				for (var i = 0; i < schemaCollection.Restrictions.Count; i++)
				{
					var restriction = schemaCollection.Restrictions[i];
					codeWriter.WriteLine($@"		dataTable.Rows.Add(""{schemaCollection.Name}"", ""{restriction.Name}"", ""{restriction.Default}"", {i + 1});");
				}
			}
		}
	}
	else
	{
		codeWriter.Write(@$"		{schema.Custom}(dataTable);
");
	}

	if (!isAsync)
	{
		codeWriter.Write(@"
		return Utility.CompletedTask;");
	}

	codeWriter.Write(@"
	}

");
}

codeWriter.Write(@"}
");

using var docWriter = new StreamWriter(@"..\..\..\..\..\docs\content\overview\schema-collections.md");
docWriter.Write($@"---
date: 2021-04-24
lastmod: {DateTime.UtcNow.ToString("yyyy-MM-dd")}
menu:
  main:
    parent: getting started
title: Schema Collections
customtitle: ""Supported Schema Collections""
weight: 80
---

# Supported Schema Collections

`DbConnection.GetSchema` retrieves schema information about the database that is currently connected. For background, see MSDN on [GetSchema and Schema Collections](https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/getschema-and-schema-collections) and [Common Schema Collections](https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/common-schema-collections).

`MySqlConnection.GetSchema` supports the following schemas:

");
foreach (var schema in schemaCollections)
	docWriter.Write($@"* `{schema.Name}`{(schema.Description is not null ? "—[" + schema.Description + "](../schema/" + schema.Name.ToLowerInvariant() + "/)" : "")}
");

foreach (var schema in schemaCollections.Where(x => x.Description is not null))
{
	using var schemaDocWriter = new StreamWriter($@"..\..\..\..\..\docs\content\overview\schema\{schema.Name.ToLowerInvariant()}.md");
	schemaDocWriter.Write($@"---
date: 2022-07-10
lastmod: {DateTime.UtcNow.ToString("yyyy-MM-dd")}
title: {schema.Name} Schema
---

# {schema.Name} Schema

The `{schema.Name}` schema provides {schema.Description}.

Column Name | Data Type | Description
--- | --- | ---
");
	foreach (var column in schema.Columns)
		schemaDocWriter.WriteLine($@"{column.Name} | {column.Type} | {column.Description}");
	schemaDocWriter.WriteLine();

	if (schema.Restrictions is { Count: > 0 })
	{
		schemaDocWriter.Write(@"The following restrictions are supported:

Restriction Name | Restriction Default | Restriction Number
--- | --- | --:
");
		for (var i = 0; i < schema.Restrictions.Count; i++)
			schemaDocWriter.WriteLine($@"{schema.Restrictions[i].Name} | {schema.Restrictions[i].Default} | {i + 1}");
		schemaDocWriter.WriteLine();
	}
}

class Schema
{
	[AllowNull]
	public string Name { get; set; }
	public string? Description { get; set; }
	public string? Custom { get; set; }
	public string? Table { get; set; }
	public int IdentifierPartCount { get; set; }
	[AllowNull]
	public List<Column> Columns { get; set; }
	public List<Restriction>? Restrictions { get; set; }
}

class Column
{
	[AllowNull]
	public string Name { get; set; }
	[AllowNull]
	public string Type { get; set; }
	public string? Description { get; set; }
	public bool Optional { get; set; }
}

class Restriction
{
	[AllowNull]
	public string Name { get; set; }
	[AllowNull]
	public string Default { get; set; }
}
