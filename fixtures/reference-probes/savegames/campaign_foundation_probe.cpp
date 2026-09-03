// Reference commit: 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15.
// Behavioral sites: Mod::newSave/getStartingBase; SavedGame::load/save/getId;
// GameTime::advance; Country, Region, Base, BaseFacility load/save methods.
//
// The redistributable scenario fixture records the externally visible observations
// extracted from those sites: five-second calendar precedence, country funding
// adjustment, area-filtered country/region creation, a 6x6 starting-base grid, and
// type/id save identities. RNG stream parity and YAML presentation are out of scope.
