﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Web;

namespace Microsoft.SemanticKernel.Plugins.OpenApi;

/// <summary>
/// The REST API operation.
/// </summary>
[Experimental("SKEXP0040")]
public sealed class RestApiOperation
{
    /// <summary>
    /// A static empty dictionary to default to when none is provided.
    /// </summary>
    private static readonly Dictionary<string, object?> s_emptyDictionary = [];

    /// <summary>
    /// Gets the name of an artificial parameter to be used for operation having "text/plain" payload media type.
    /// </summary>
    public static string PayloadArgumentName => "payload";

    /// <summary>
    /// Gets the name of an artificial parameter to be used for indicate payload media-type if it's missing in payload metadata.
    /// </summary>
    public static string ContentTypeArgumentName => "content-type";

    /// <summary>
    /// The operation identifier.
    /// </summary>
    public string? Id { get; }

    /// <summary>
    /// The operation description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// The operation path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The operation method - GET, POST, PUT, DELETE.
    /// </summary>
    public HttpMethod Method { get; }

    /// <summary>
    /// The server.
    /// </summary>
    public IReadOnlyList<RestApiServer> Servers { get; }

    /// <summary>
    /// The security requirements.
    /// </summary>
    public IReadOnlyList<RestApiSecurityRequirement> SecurityRequirements { get; }

    /// <summary>
    /// The operation parameters.
    /// </summary>
    public IReadOnlyList<RestApiParameter> Parameters { get; }

    /// <summary>
    /// The list of possible operation responses.
    /// </summary>
    public IReadOnlyDictionary<string, RestApiExpectedResponse> Responses { get; }

    /// <summary>
    /// The operation payload.
    /// </summary>
    public RestApiPayload? Payload { get; }

    /// <summary>
    /// Additional unstructured metadata about the operation.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } = s_emptyDictionary;

    /// <summary>
    /// Creates an instance of a <see cref="RestApiOperation"/> class.
    /// </summary>
    /// <param name="id">The operation identifier.</param>
    /// <param name="servers">The servers.</param>
    /// <param name="path">The operation path.</param>
    /// <param name="method">The operation method.</param>
    /// <param name="description">The operation description.</param>
    /// <param name="parameters">The operation parameters.</param>
    /// <param name="responses">The operation responses.</param>
    /// <param name="securityRequirements">The operation security requirements.</param>
    /// <param name="payload">The operation payload.</param>
    internal RestApiOperation(
        string? id,
        IReadOnlyList<RestApiServer> servers,
        string path,
        HttpMethod method,
        string? description,
        IReadOnlyList<RestApiParameter> parameters,
        IReadOnlyDictionary<string, RestApiExpectedResponse> responses,
        IReadOnlyList<RestApiSecurityRequirement> securityRequirements,
        RestApiPayload? payload = null)
    {
        this.Id = id;
        this.Servers = servers;
        this.Path = path;
        this.Method = method;
        this.Description = description;
        this.Parameters = parameters;
        this.Responses = responses;
        this.SecurityRequirements = securityRequirements;
        this.Payload = payload;
        this.Responses = responses ?? new Dictionary<string, RestApiExpectedResponse>();
        this.SecurityRequirements = securityRequirements;
    }

    /// <summary>
    /// Builds operation Url.
    /// </summary>
    /// <param name="arguments">The operation arguments.</param>
    /// <param name="serverUrlOverride">Override for REST API operation server url.</param>
    /// <param name="apiHostUrl">The URL of REST API host.</param>
    /// <returns>The operation Url.</returns>
    internal Uri BuildOperationUrl(IDictionary<string, object?> arguments, Uri? serverUrlOverride = null, Uri? apiHostUrl = null)
    {
        var serverUrl = this.GetServerUrl(serverUrlOverride, apiHostUrl, arguments);

        var path = this.BuildPath(this.Path, arguments);

        return new Uri(serverUrl, $"{path.TrimStart('/')}");
    }

    /// <summary>
    /// Builds operation request headers.
    /// </summary>
    /// <param name="arguments">The operation arguments.</param>
    /// <returns>The request headers.</returns>
    internal IDictionary<string, string> BuildHeaders(IDictionary<string, object?> arguments)
    {
        var headers = new Dictionary<string, string>();

        var parameters = this.Parameters.Where(p => p.Location == RestApiParameterLocation.Header);

        foreach (var parameter in parameters)
        {
            if (!arguments.TryGetValue(parameter.Name, out object? argument) || argument is null)
            {
                // Throw an exception if the parameter is a required one but no value is provided.
                if (parameter.IsRequired)
                {
                    throw new KernelException($"No argument is provided for the '{parameter.Name}' required parameter of the operation - '{this.Id}'.");
                }

                // Skipping not required parameter if no argument provided for it.
                continue;
            }

            var parameterStyle = parameter.Style ?? RestApiParameterStyle.Simple;

            if (!s_parameterSerializers.TryGetValue(parameterStyle, out var serializer))
            {
                throw new KernelException($"The headers parameter '{parameterStyle}' serialization style is not supported.");
            }

            var node = OpenApiTypeConverter.Convert(parameter.Name, parameter.Type, argument);

            //Serializing the parameter and adding it to the headers.
            headers.Add(parameter.Name, serializer.Invoke(parameter, node));
        }

        return headers;
    }

    /// <summary>
    /// Builds the operation query string.
    /// </summary>
    /// <param name="arguments">The operation arguments.</param>
    /// <returns>The query string.</returns>
    internal string BuildQueryString(IDictionary<string, object?> arguments)
    {
        var segments = new List<string>();

        var parameters = this.Parameters.Where(p => p.Location == RestApiParameterLocation.Query);

        foreach (var parameter in parameters)
        {
            if (!arguments.TryGetValue(parameter.Name, out object? argument) || argument is null)
            {
                // Throw an exception if the parameter is a required one but no value is provided.
                if (parameter.IsRequired)
                {
                    throw new KernelException($"No argument or value is provided for the '{parameter.Name}' required parameter of the operation - '{this.Id}'.");
                }

                // Skipping not required parameter if no argument provided for it.
                continue;
            }

            var parameterStyle = parameter.Style ?? RestApiParameterStyle.Form;

            if (!s_parameterSerializers.TryGetValue(parameterStyle, out var serializer))
            {
                throw new KernelException($"The query string parameter '{parameterStyle}' serialization style is not supported.");
            }

            var node = OpenApiTypeConverter.Convert(parameter.Name, parameter.Type, argument);

            // Serializing the parameter and adding it to the query string if there's an argument for it.
            segments.Add(serializer.Invoke(parameter, node));
        }

        return string.Join("&", segments);
    }

    #region private

    /// <summary>
    /// Builds operation path.
    /// </summary>
    /// <param name="pathTemplate">The original path template.</param>
    /// <param name="arguments">The operation arguments.</param>
    /// <returns>The path.</returns>
    private string BuildPath(string pathTemplate, IDictionary<string, object?> arguments)
    {
        var parameters = this.Parameters.Where(p => p.Location == RestApiParameterLocation.Path);

        foreach (var parameter in parameters)
        {
            if (!arguments.TryGetValue(parameter.Name, out object? argument) || argument is null)
            {
                // Throw an exception if the parameter is a required one but no value is provided.
                if (parameter.IsRequired)
                {
                    throw new KernelException($"No argument is provided for the '{parameter.Name}' required parameter of the operation - '{this.Id}'.");
                }

                // Skipping not required parameter if no argument provided for it.
                continue;
            }

            var parameterStyle = parameter.Style ?? RestApiParameterStyle.Simple;

            if (!s_parameterSerializers.TryGetValue(parameterStyle, out var serializer))
            {
                throw new KernelException($"The path parameter '{parameterStyle}' serialization style is not supported.");
            }

            var node = OpenApiTypeConverter.Convert(parameter.Name, parameter.Type, argument);

            // Serializing the parameter and adding it to the path.
            pathTemplate = pathTemplate.Replace($"{{{parameter.Name}}}", HttpUtility.UrlEncode(serializer.Invoke(parameter, node)));
        }

        return pathTemplate;
    }

    /// <summary>
    /// Returns operation server Url.
    /// </summary>
    /// <param name="serverUrlOverride">Override for REST API operation server url.</param>
    /// <param name="apiHostUrl">The URL of REST API host.</param>
    /// <param name="arguments">The operation arguments.</param>
    /// <returns>The operation server url.</returns>
    private Uri GetServerUrl(Uri? serverUrlOverride, Uri? apiHostUrl, IDictionary<string, object?> arguments)
    {
        string serverUrlString;

        if (serverUrlOverride is not null)
        {
            serverUrlString = serverUrlOverride.AbsoluteUri;
        }
        else if (this.Servers is { Count: > 0 } servers && servers[0].Url is { } url)
        {
            serverUrlString = url;
            foreach (var variable in servers[0].Variables)
            {
                arguments.TryGetValue(variable.Key, out object? value);
                string? strValue = value as string;
                if (strValue is not null && variable.Value.IsValid(strValue))
                {
                    serverUrlString = serverUrlString.Replace($"{{{variable.Key}}}", strValue);
                }
                else if (variable.Value.Default is not null)
                {
                    serverUrlString = serverUrlString.Replace($"{{{variable.Key}}}", variable.Value.Default);
                }
                else
                {
                    throw new KernelException($"No value provided for the '{variable.Key}' server variable of the operation - '{this.Id}'.");
                }
            }
        }
        else
        {
            serverUrlString =
                apiHostUrl?.AbsoluteUri ??
                throw new InvalidOperationException($"Server url is not defined for operation {this.Id}");
        }

        // Make sure base url ends with trailing slash
        if (!serverUrlString.EndsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            serverUrlString += "/";
        }

        return new Uri(serverUrlString);
    }

    private static readonly Dictionary<RestApiParameterStyle, Func<RestApiParameter, JsonNode, string>> s_parameterSerializers = new()
    {
        { RestApiParameterStyle.Simple, SimpleStyleParameterSerializer.Serialize },
        { RestApiParameterStyle.Form, FormStyleParameterSerializer.Serialize },
        { RestApiParameterStyle.SpaceDelimited, SpaceDelimitedStyleParameterSerializer.Serialize },
        { RestApiParameterStyle.PipeDelimited, PipeDelimitedStyleParameterSerializer.Serialize }
    };

    # endregion
}
