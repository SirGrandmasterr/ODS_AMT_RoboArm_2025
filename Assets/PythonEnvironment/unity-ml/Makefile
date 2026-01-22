.PHONY: win-direct

# Target to run ML-Agents training using poetry, specifically configured for Windows/direct path structure.
win-direct:
	python -m poetry run mlagents-learn ..\..\..\config\training_config.yaml --force

win-IK:
	python -m poetry run mlagents-learn ..\..\training_config_ik.yaml --force
win-imitation:
	python -m poetry run mlagents-learn ..\..\training_config_imitation.yaml --force