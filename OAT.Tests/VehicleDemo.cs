﻿using Microsoft.CST.OAT.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MoreLinq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.CST.OAT.Tests
{
    [TestClass]
    public class VehicleDemo
    {
        class Vehicle
        {
            public int Weight;
            public int Axles { get; set; }
            public int Occupants { get; set; }
            public int Capacity { get; set; }
            public Driver? Driver { get; set; }
        }

        [ClassInitialize]
        public static void ClassSetup(TestContext _)
        {
            Logger.SetupVerbose();
            Strings.Setup();
        }

        class Driver
        {
            public DriverLicense? License { get; set; }
        }

        class DriverLicense
        {
            public Endorsements Endorsements { get; set; }
            public DateTime Expiration { get; set; }
        }

        [Flags]
        enum Endorsements
        {
            Motorcycle = 1,
            Auto = 2,
            CDL = 4
        }

        int GetCost(Vehicle vehicle, Analyzer analyzer, IEnumerable<Rule> rules)
        {
            // This gets the maximum severity rule that is applied and gets the cost of that rule, if no rules 0 cost
            return ((VehicleRule)analyzer.Analyze(rules, vehicle).MaxBy(x => x.Severity).FirstOrDefault())?.Cost ?? 0;
        }

        public (bool Applies, bool Result, ClauseCapture? Capture) OverweightOperationDelegate(Clause clause, object? state1, object? state2)
        {
            if (clause.CustomOperation == "OVERWEIGHT")
            {
                if (state1 is Vehicle vehicle)
                {
                    var res = vehicle.Weight > vehicle.Capacity;
                    if ((res && !clause.Invert) || (clause.Invert && !res))
                    {
                        // The rule applies and is true and the capture is available if capture is enabled
                        return (true, true, clause.Capture ? new TypedClauseCapture<int>(clause, vehicle.Weight, state1, state2) : null);
                    }
                }
                // The rule applies but is false
                return (true, false, null);
            }
            // The rule doesn't apply
            return (false, false, null);
        }

        public (bool Applies, IEnumerable<Violation> Violations) OverweightOperationValidationDelegate(Rule r, Clause c)
        {
            if (c.CustomOperation == "OVERWEIGHT")
            {
                var violations = new List<Violation>();
                if (r.Target != "Vehicle")
                {
                    violations.Add(new Violation("Overweight operation requires a Vehicle object", r, c));
                }

                if (c.Data != null || c.DictData != null)
                {
                    violations.Add(new Violation("Overweight operation takes no data.", r, c));
                }
                return (true, violations);
            }
            else
            {
                return (false, Array.Empty<Violation>());
            }
        }

        public class VehicleRule : Rule
        {
            public int Cost;
            public VehicleRule(string name) : base(name) { }
        }

        [TestMethod]
        public void WeighStationDemo()
        {
            var truck = new Vehicle()
            {
                Weight = 20000,
                Capacity = 20000,
                Driver = new Driver()
                {
                    License = new DriverLicense()
                    {
                        Endorsements = Endorsements.CDL | Endorsements.Auto,
                        Expiration = DateTime.Now.AddYears(1)
                    }
                }
            };

            var overweightTruck = new Vehicle()
            {
                Weight = 30000,
                Capacity = 20000,
                Driver = new Driver()
                {
                    License = new DriverLicense()
                    {
                        Endorsements = Endorsements.CDL | Endorsements.Auto,
                        Expiration = DateTime.Now.AddYears(1)
                    }
                }
            };

            var expiredLicense = new Vehicle()
            {
                Weight = 20000,
                Capacity = 20000,
                Driver = new Driver()
                {
                    License = new DriverLicense()
                    {
                        Endorsements = Endorsements.CDL | Endorsements.Auto,
                        Expiration = DateTime.Now.AddYears(-1)
                    }
                }
            };

            var noCdl = new Vehicle()
            {
                Weight = 20000,
                Capacity = 20000,
                Driver = new Driver()
                {
                    License = new DriverLicense()
                    {
                        Endorsements = Endorsements.Auto,
                        Expiration = DateTime.Now.AddYears(1)
                    }
                }
            };

            var rules = new VehicleRule[] {
                new VehicleRule("Overweight")
                {
                    Cost = 50,
                    Severity = 9,
                    Expression = "Overweight",
                    Target = "Vehicle",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.Custom)
                        {
                            Label = "Overweight",
                            CustomOperation = "OVERWEIGHT",
                            Capture = true
                        }
                    }
                },
                new VehicleRule("No CDL")
                {
                    Cost = 100,
                    Severity = 3,
                    Target = "Vehicle",
                    Expression = "NOT Has_Cdl",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.Contains, "Driver.License.Endorsements")
                        {
                            Label = "Has_Cdl",
                            Data = new List<string>()
                            {
                                "CDL"
                            }
                        }
                    }
                },
                new VehicleRule("Expired License"){
                    Cost = 75,
                    Severity = 1,
                    Target = "Vehicle",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.IsExpired, "Driver.License.Expiration")
                        {
                        }
                    }
                }
            };
            var analyzer = new Analyzer();
            analyzer.CustomOperationDelegates.Add(OverweightOperationDelegate);
            analyzer.CustomOperationValidationDelegates.Add(OverweightOperationValidationDelegate);

            var issues = analyzer.EnumerateRuleIssues(rules).ToList();

            Assert.IsFalse(issues.Any());

            Assert.IsTrue(!analyzer.Analyze(rules, truck).Any()); // Compliant
            Assert.IsTrue(analyzer.Analyze(rules, overweightTruck).Any(x => x.Name == "Overweight")); // Overweight
            Assert.IsTrue(analyzer.Analyze(rules, noCdl).Any(x => x.Name == "No CDL")); // Overweight
            Assert.IsTrue(analyzer.Analyze(rules, expiredLicense).Any(x => x.Name == "Expired License")); // Overweight

            var res = analyzer.GetCaptures(rules, overweightTruck);
            var weight = ((TypedClauseCapture<int>)res.First().Captures[0]).Result;

            Assert.IsTrue(weight == 30000);
        }

        [TestMethod]
        public void TollBoothDemo()
        {
            var truck = new Vehicle()
            {
                Weight = 20000,
                Capacity = 20000,
                Axles = 5,
                Occupants = 1
            };

            var overweightTruck = new Vehicle()
            {
                Weight = 30000,
                Capacity = 20000,
                Axles = 5,
                Occupants = 1
            };

            var car = new Vehicle()
            {
                Weight = 3000,
                Axles = 2,
                Occupants = 1
            };

            var carpool = new Vehicle()
            {
                Weight = 3000,
                Axles = 2,
                Occupants = 3
            };

            var motorcycle = new Vehicle()
            {
                Weight = 1000,
                Axles = 2,
                Occupants = 1
            };

            var rules = new VehicleRule[] {
                new VehicleRule("Overweight")
                {
                    Cost = 50,
                    Severity = 9,
                    Expression = "Overweight AND gt2Axles",
                    Target = "Vehicle",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.Custom)
                        {
                            Label = "Overweight",
                            CustomOperation = "OVERWEIGHT"
                        },
                        new Clause(Operation.GreaterThan, "Axles")
                        {
                            Label = "gt2Axles",
                            Data = new List<string>()
                            {
                                "2"
                            }
                        }
                    }
                },
                new VehicleRule("Heavy or long")
                {
                    Cost = 10,
                    Severity = 3,
                    Expression = "Weight OR Axles",
                    Target = "Vehicle",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.GreaterThan, "Weight")
                        {
                            Label = "Weight",
                            Data = new List<string>()
                            {
                                "4000"
                            }
                        },
                        new Clause(Operation.GreaterThan, "Axles")
                        {
                            Label = "Axles",
                            Data = new List<string>()
                            {
                                "2"
                            }
                        }
                    }
                },
                new VehicleRule("Normal Car"){
                    Cost = 3,
                    Severity = 1,
                    Target = "Vehicle",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.GreaterThan, "Weight")
                        {
                            Data = new List<string>()
                            {
                                "1000"
                            }
                        }
                    }
                },
                new VehicleRule("Carpool Car"){
                    Cost = 2,
                    Severity = 2,
                    Target = "Vehicle",
                    Expression = "WeightGT1000 AND WeightLT4000 AND OccupantsGT2",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.GreaterThan, "Weight")
                        {
                            Label = "WeightGT1000",
                            Data = new List<string>()
                            {
                                "1000"
                            }
                        },
                        new Clause(Operation.LessThan, "Weight")
                        {
                            Label = "WeightLT4000",
                            Data = new List<string>()
                            {
                                "4000"
                            }
                        },
                        new Clause(Operation.GreaterThan, "Occupants")
                        {
                            Label = "OccupantsGT2",
                            Data = new List<string>()
                            {
                                "2"
                            }
                        },
                    }
                },
                new VehicleRule("Motorcycle"){
                    Cost = 1,
                    Severity = 0,
                    Target = "Vehicle",
                    Clauses = new List<Clause>()
                    {
                        new Clause(Operation.LessThan, "Weight")
                        {
                            Data = new List<string>()
                            {
                                "1001"
                            }
                        }
                    }
                }
            };
            var analyzer = new Analyzer();
            analyzer.CustomOperationDelegates.Add(OverweightOperationDelegate);
            analyzer.CustomOperationValidationDelegates.Add(OverweightOperationValidationDelegate);

            var issues = analyzer.EnumerateRuleIssues(rules).ToList();

            Assert.IsFalse(issues.Any());

            Assert.IsTrue(GetCost(overweightTruck, analyzer, rules) == 50);
            Assert.IsTrue(GetCost(truck, analyzer, rules) == 10);// 10
            Assert.IsTrue(GetCost(car, analyzer, rules) == 3); // 3
            Assert.IsTrue(GetCost(carpool, analyzer, rules) == 2); // 2 
            Assert.IsTrue(GetCost(motorcycle, analyzer, rules) == 1); // 1
        }
    }
}