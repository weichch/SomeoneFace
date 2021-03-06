﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Harcourts.Face.WebsiteCommon.Lookups;
using Harcourts.Face.WebsiteCommon.Models;
using Harcourts.Face.WebsiteService.Mapping;
using Newtonsoft.Json.Linq;

namespace Harcourts.Face.WebsiteService.Lookups
{
    /// <summary>
    /// Implements the person lookup by searching the persisted JSON files.
    /// </summary>
    public sealed class JsonPersonLookup : IPersonLookup<Guid>
    {
        private ILookup<Guid, Person> _personProjectionLookup;
        private readonly IJsonFileProvider _fileProvider;

        /// <summary>
        /// Initializes an instance of the lookup.
        /// </summary>
        public JsonPersonLookup() 
            : this(new HttpContextJsonFileProvider())
        {
        }

        /// <summary>
        /// Initializes an instance of the lookup.
        /// </summary>
        /// <param name="fileProvider">The data file provider.</param>
        public JsonPersonLookup(IJsonFileProvider fileProvider)
        {
            _fileProvider = fileProvider;
        }

        /// <summary>
        /// Finds the persons with the specified person IDs.
        /// </summary>
        /// <param name="personIds">The IDs of the persons.</param>
        public Task<IEnumerable<Person>> Find(IEnumerable<PersonLookupKey<Guid>> personIds)
        {
            Contract.Ensures(Contract.Result<IEnumerable<Person>>() != null);

            if (personIds == null)
            {
                throw new ArgumentNullException("key");
            }
            var personIdList = personIds.ToList();
            if (personIdList.Any(x => x.Key == null))
            {
                throw new ArgumentException("At least one underlying key of the keys is null.");
            }

            var projectionLookup = EnsurePersonLookup();
            var results = personIdList.Select(
                personId => (IEnumerable<Person>)
                    (projectionLookup.Contains(personId.Key)
                        ? projectionLookup[personId.Key]
                        : Enumerable.Empty<Person>()))
                .ToList()
                .SelectMany(x => x)
                ;
            return Task.FromResult(results);
        }

        /// <summary>
        /// Reads the JSON file and return all existing persons.
        /// </summary>
        private IReadOnlyDictionary<string, dynamic> ReadExistingPersons()
        {
            var dictionary = ReadJsonDataFile("existingPersons.json")
                .ToDictionary(x => (string) x.userData)
                .AsReadOnly()
                ;
            return dictionary;
        }

        /// <summary>
        /// Reads the JSON file and return all current scrawl results.
        /// </summary>
        private IReadOnlyDictionary<string, dynamic> ReadScrawlResults()
        {
            var dictionary = ReadJsonDataFile("scrawlResult.json")
                .ToDictionary(x => (string) x.personName + "|" + (string) x.emailAddress)
                .AsReadOnly()
                ;
            return dictionary;
        }

        /// <summary>
        /// Reads in persisted JSON file.
        /// </summary>
        /// <param name="fileName">The file name relative to the App_Data folder.</param>
        private IEnumerable<dynamic> ReadJsonDataFile(string fileName)
        {
            var filePath = _fileProvider.GetDataFilePath(fileName);
            var json = File.ReadAllText(filePath);
            var array = JArray.Parse(json);
            var items = array.Children<JObject>()
                .Select(x => x.ToObject<dynamic>())
                .ToList()
                .AsReadOnly()
                ;
            return items;
        }

        /// <summary>
        /// Projects the stored data and generates a lookup for person.
        /// </summary>
        private ILookup<Guid, Person> EnsurePersonLookup()
        {
            if (_personProjectionLookup != null)
            {
                return _personProjectionLookup;
            }

            var scrawlResults = ReadScrawlResults();
            var existingPersons = ReadExistingPersons();

            var pocoMapper = new ScrawlResultPersonMapper();
            var projectionLookup = scrawlResults.Values
                .Join(
                    existingPersons.Values,
                    r => ((string) r.personName + "|" + (string) r.emailAddress),
                    p => ((string) p.userData),
                    (r, p) => new {r, p})
                .ToLookup(
                    x => new Guid((string) x.p.personId),
                    x => new Person(pocoMapper.Map(x.r, new PersonPoco {PersonIdentity = x.p.personId})))
                ;

            _personProjectionLookup = projectionLookup;
            return _personProjectionLookup;
        }

        /// <summary>
        /// Returns all items as an enumerable.
        /// </summary>
        public IEnumerable<Person> AsEnumerable()
        {
            var lookup = EnsurePersonLookup();
            var enumerable = lookup.SelectMany(x => x.AsEnumerable());
            return enumerable;
        }
    }
}
