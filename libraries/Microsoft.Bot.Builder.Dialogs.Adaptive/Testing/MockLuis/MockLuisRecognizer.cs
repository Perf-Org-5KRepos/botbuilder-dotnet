﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Recognizers;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using RichardSzalay.MockHttp;

namespace Microsoft.Bot.Builder.MockLuis
{
    /// <summary>
    /// Test class for creating cached LUIS responses for testing.
    /// </summary>
    /// <remarks>
    /// This will either use a cached LUIS response or generate a new one by calling LUIS.
    /// </remarks>
    public class MockLuisRecognizer : Recognizer
    {
        private string _responseDir;
        private LuisAdaptiveRecognizer _recognizer;

        /// <summary>
        /// Initializes a new instance of the <see cref="MockLuisRecognizer"/> class.
        /// </summary>
        /// <param name="recognizer">LUIS recognizer definition.</param>
        /// <param name="resourceDir">Where the settings file generated by lubuild is found.</param>
        /// <param name="name">Name of the LUIS model.</param>
        public MockLuisRecognizer(
            LuisAdaptiveRecognizer recognizer,
            string resourceDir,
            string name)
        {
            _recognizer = recognizer;
            _responseDir = Path.Combine(resourceDir, "cachedResponses", name);
            if (!Directory.Exists(_responseDir))
            {
                Directory.CreateDirectory(_responseDir);
            }
        }

        public override async Task<RecognizerResult> RecognizeAsync(DialogContext dialogContext, Activity activity, CancellationToken cancellationToken = default)
        {
            var recognizer = _recognizer.RecognizerOptions(dialogContext);
            recognizer.IncludeAPIResults = true;
            var client = GetMockedClient(activity.Text, recognizer);
            var wrapper = new LuisRecognizer(recognizer, client);
            var result = await wrapper.RecognizeAsync(dialogContext.Context, cancellationToken).ConfigureAwait(false);
            if (client == null)
            {
                // Save response
                var outPath = ResponsePath(activity.Text, recognizer);
                File.WriteAllText(outPath, JsonConvert.SerializeObject(result.Properties["luisResult"]));
            }

            return result;
        }

        private string ResponsePath(string utterance, LuisRecognizerOptionsV3 recognizer)
        {
            var hash = utterance.StableHash();
            if (recognizer.ExternalEntityRecognizer != null)
            {
                hash ^= "external".StableHash();
            }

            if (recognizer.IncludeAPIResults)
            {
                hash ^= "api".StableHash();
            }

            if (recognizer.LogPersonalInformation)
            {
                hash ^= "personal".StableHash();
            }

            var options = recognizer.PredictionOptions;
            if (options.DynamicLists != null)
            {
                foreach (var dynamicList in options.DynamicLists)
                {
                    hash ^= dynamicList.Entity.StableHash();
                    foreach (var choices in dynamicList.List)
                    {
                        hash ^= choices.CanonicalForm.StableHash();
                        foreach (var synonym in choices.Synonyms)
                        {
                            hash ^= synonym.StableHash();
                        }
                    }
                }
            }

            if (options.ExternalEntities != null)
            {
                foreach (var external in options.ExternalEntities)
                {
                    hash ^= external.Entity.StableHash();
                    hash ^= external.Start.ToString().StableHash();
                    hash ^= external.Length.ToString().StableHash();
                }
            }

            if (options.IncludeAllIntents)
            {
                hash ^= "all".StableHash();
            }

            if (options.IncludeInstanceData)
            {
                hash ^= "instance".StableHash();
            }

            if (options.Log ?? false)
            {
                hash ^= "log".StableHash();
            }

            if (options.PreferExternalEntities)
            {
                hash ^= "prefer".StableHash();
            }

            if (options.Slot != null)
            {
                hash ^= options.Slot.StableHash();
            }

            if (options.Version != null)
            {
                hash ^= options.Version.StableHash();
            }

            return Path.Combine(_responseDir, $"{hash}.json");
        }

        private HttpClientHandler GetMockedClient(string utterance, LuisRecognizerOptionsV3 recognizer)
        {
            HttpClientHandler client = null;
            if (utterance != null)
            {
                var response = ResponsePath(utterance, recognizer);
                if (File.Exists(response))
                {
                    var handler = new MockHttpMessageHandler();
                    handler
                        .When(recognizer.Application.Endpoint + "*")
                        .WithPartialContent(utterance)
                        .Respond("application/json", File.OpenRead(response));
                    client = new MockedHttpClientHandler(handler.ToHttpClient());
                }
            }

            return client;
        }
    }
}
