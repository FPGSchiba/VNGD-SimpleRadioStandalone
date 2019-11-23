local DbOption = require("Options.DbOption")

return {
  srsAutoLaunchEnabled = DbOption.new():setValue(true):checkbox(),
  srsOverlayEnabled = DbOption.new():setValue(true):checkbox()
}
