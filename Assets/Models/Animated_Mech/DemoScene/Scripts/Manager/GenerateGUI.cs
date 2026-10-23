using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GenerateGUI : MonoBehaviour {

    public ItemsHolder itemsHolder;
    public PresetCharacter presets;
    public Animation characterAnimation;
    public Transform leftHolder;
    public Transform rightHolder;
    public Transform assetHolder;
    public OptionsHolder optionHodler;

    private Animation currentCharacterAnimation;
    private OptionsHolder presetOptions;
    private Transform animationsHolder;

    private void Start()
    {
        if(itemsHolder != null)
           GenerateItemsUI();

        if (presets != null)
            GeneratePresetUI();

        if (characterAnimation != null)
        {
            currentCharacterAnimation = characterAnimation;
            GenerateAnimationsUI();
        }
    }

    //-----------------------------------------------------------------
    private void GenerateItemsUI()
    {
        for(int i = 0; i < itemsHolder.itemsConfiguration.Count; i++)
        {
            var itemIndex = i;

            var assetHolderInstance = Instantiate(assetHolder, leftHolder);

            var holderName = assetHolderInstance.GetComponentInChildren<Text>();

            if (holderName != null)
                holderName.text = itemsHolder.itemsConfiguration[itemIndex].displayName;

            var optionsInstance = Instantiate(optionHodler, assetHolderInstance);

            var optionsNames = new List<string>();

            foreach (var option in itemsHolder.itemsConfiguration[itemIndex].items)
            {
                if (option.model != null)
                {
                    optionsNames.Add(option.model.name);
                }
                else
                {
                    optionsNames.Add("None");
                }
            }

            var materialOptions = new List<Transform>();

            optionsInstance.assetList.AddOptions(optionsNames);
            optionsInstance.assetList.value = itemsHolder.itemsConfiguration[itemIndex].GetCounter;
            optionsInstance.assetList.onValueChanged.AddListener(delegate { ActivateModel(itemIndex, optionsInstance, materialOptions, assetHolderInstance); });
            optionsInstance.nextButton.onClick.AddListener(delegate { NextModel(itemIndex, optionsInstance, materialOptions, assetHolderInstance); });
            optionsInstance.previousButton.onClick.AddListener(delegate { PreviousModel(itemIndex, optionsInstance, materialOptions, assetHolderInstance); });
            ActivateModel(itemIndex, optionsInstance, materialOptions, assetHolderInstance);
        }
    }

    public void NextModel(int elementIndex, OptionsHolder optionsDropdown, List<Transform> materialOptions, Transform assetHolder)
    {
        if (optionsDropdown.assetList.value < optionsDropdown.assetList.options.Count)
        {
            optionsDropdown.assetList.value++;
            ActivateModel(elementIndex, optionsDropdown, materialOptions, assetHolder);
        }
    }

    public void PreviousModel(int elementIndex, OptionsHolder optionsDropdown, List<Transform> materialOptions, Transform assetHolder)
    {
        if (optionsDropdown.assetList.value > 0)
        {
            optionsDropdown.assetList.value--;
            ActivateModel(elementIndex, optionsDropdown, materialOptions, assetHolder);
        }
    }

    public void ActivateModel(int elementIndex, OptionsHolder optionsHolder, List<Transform> materialOptions, Transform assetHolder)
    {
        //If we used presets our character is currently inactive therefore we need to switch it on
        ActivateCharacter();

        itemsHolder.itemsConfiguration[elementIndex].ActivateModel(optionsHolder.assetList.value);
        optionsHolder.CheckButtons();

        //Clearing material options 
        foreach (var materialOption in materialOptions)
            if (materialOption != null)
                Destroy(materialOption.gameObject);

        //Generating material options for given item
        if (itemsHolder.itemsConfiguration[elementIndex].items[optionsHolder.assetList.value].materials != null)
        {
            for (int i = 0; i < itemsHolder.itemsConfiguration[elementIndex].items[optionsHolder.assetList.value].materials.Count; i++)
            {
                var materialIndex = i;
                var instantiatedMaterialOption = Instantiate(optionHodler, assetHolder);
                materialOptions.Add(instantiatedMaterialOption.transform);

                var optionsNames = new List<string>();

                foreach (var option in itemsHolder.itemsConfiguration[elementIndex].items[optionsHolder.assetList.value].materials[i].aviableMaterials)
                {
                    optionsNames.Add(option.name);
                }

                instantiatedMaterialOption.assetList.AddOptions(optionsNames);
                instantiatedMaterialOption.assetList.value = itemsHolder.itemsConfiguration[elementIndex].items[optionsHolder.assetList.value].materials[materialIndex].GetCounter;
                instantiatedMaterialOption.assetList.onValueChanged.AddListener(delegate { ChangeMaterial(elementIndex, materialIndex, instantiatedMaterialOption); });
                instantiatedMaterialOption.nextButton.onClick.AddListener(delegate { NextMaterial(elementIndex, materialIndex, instantiatedMaterialOption); });
                instantiatedMaterialOption.previousButton.onClick.AddListener(delegate { PreviousMaterial(elementIndex, materialIndex, instantiatedMaterialOption); });
                ChangeMaterial(elementIndex, materialIndex, instantiatedMaterialOption);
            }
        }
    }

    //-----------------------------------------------------------------
    public void NextMaterial(int meshIndex, int objectIndex, OptionsHolder optionsHolder)
    {
        if (optionsHolder.assetList.value < optionsHolder.assetList.options.Count - 1)
        {
            optionsHolder.assetList.value++;
            ChangeMaterial(meshIndex, objectIndex, optionsHolder);
        }
    }

    public void PreviousMaterial(int meshIndex, int objectIndex, OptionsHolder optionsHolder)
    {
        if (optionsHolder.assetList.value > 0)
        {
            optionsHolder.assetList.value--;
            ChangeMaterial(meshIndex, objectIndex, optionsHolder);
        }
    }

    public void ChangeMaterial(int meshIndex, int objectIndex, OptionsHolder optionsHodler)
    {
        ActivateCharacter();
        optionsHodler.CheckButtons();
        itemsHolder.itemsConfiguration[meshIndex].ChangeMaterial(objectIndex, optionsHodler.assetList.value);
    }

    //-----------------------------------------------------------------
    private void GenerateAnimationsUI()
    {
        if (animationsHolder != null)
            Destroy(animationsHolder.gameObject);

        var defaultAnimation = currentCharacterAnimation.clip.name;
        var animationsNames = new List<string>();
        int defaultAnimationIndex = 0;
        int counter = 0;

        foreach (AnimationState state in currentCharacterAnimation)
        {
            animationsNames.Add(state.name);

			if (state.name != currentCharacterAnimation.clip.name)
                counter++;
            else
                defaultAnimationIndex = counter;
        }

        var assetHolderInstance = Instantiate(assetHolder, rightHolder);

        var holderName = assetHolderInstance.GetComponentInChildren<Text>();

        if (holderName != null)
            holderName.text = "Animations";

        var optionsInstance = Instantiate(optionHodler, assetHolderInstance);

        optionsInstance.assetList.AddOptions(animationsNames);
        optionsInstance.assetList.value = defaultAnimationIndex;
        optionsInstance.assetList.onValueChanged.AddListener(delegate { PlayAnimation(optionsInstance); });
        optionsInstance.nextButton.onClick.AddListener(delegate { NextPlayAnimation(optionsInstance); });
        optionsInstance.previousButton.onClick.AddListener(delegate { PreviousPlayAnimation(optionsInstance); });
        animationsHolder = assetHolderInstance;
    }

    public void NextPlayAnimation(OptionsHolder optionsHolder)
    {
        if (optionsHolder.assetList.value < optionsHolder.assetList.options.Count)
        {
            optionsHolder.assetList.value++;
            PlayAnimation(optionsHolder);
        }
    }

    public void PreviousPlayAnimation(OptionsHolder optionsHolder)
    {
        if (optionsHolder.assetList.value > 0)
        {
            optionsHolder.assetList.value--;
            PlayAnimation(optionsHolder);
        }
    }

    public void PlayAnimation(OptionsHolder optionsHolder)
    {
        currentCharacterAnimation.Play(optionsHolder.assetList.options[optionsHolder.assetList.value].text);
        optionsHolder.CheckButtons();
    }

    //-----------------------------------------------------------------
    private void GeneratePresetUI()
    {

        var assetHolderInstance = Instantiate(assetHolder, rightHolder);

        var holderName = assetHolderInstance.GetComponentInChildren<Text>();

        if (holderName != null)
            holderName.text = "Presets";

        var optionsInstance = Instantiate(optionHodler, assetHolderInstance);

        var presetsNames = new List<string>();
        presetsNames.Add("None");

        foreach (var preset in presets.characters)
        {
           if(preset != null)
           {
                presetsNames.Add(preset.name);
           }
        }

        optionsInstance.assetList.AddOptions(presetsNames);
        optionsInstance.assetList.value = 0;
        presetOptions = optionsInstance;
        presetOptions.CheckButtons();

        optionsInstance.assetList.onValueChanged.AddListener(delegate { ActivatePreset(); });
        optionsInstance.nextButton.onClick.AddListener(delegate { ActivateNextPreset(); });
        optionsInstance.previousButton.onClick.AddListener(delegate { ActivatePreviousPreset(); });
    }

    public void ActivateNextPreset()
    {
        if(presetOptions.assetList.value < presetOptions.assetList.options.Count)
        {
            presetOptions.assetList.value++;
            ActivatePreset();
        }
    }

    public void ActivatePreviousPreset()
    {
        if(presetOptions.assetList.value > 0)
        {
            presetOptions.assetList.value--;
            ActivatePreset();
        }
    }

    public void ActivatePreset()
    {
        if(presetOptions.assetList.value != 0) //Option 0 is "None"
        {
            if (characterAnimation != null)
                characterAnimation.gameObject.SetActive(false);

            presets.ActivateCharacter(presetOptions.assetList.value - 1, characterAnimation.transform.position);

            var characterRotate = GetComponent<CharacterRotate>();

            if (characterRotate != null)
                characterRotate.objectToRotate = presets.GetCurrentCharacter;

            var presetAnimations = presets.GetCurrentCharacter.GetComponent<Animation>();
            if (presetAnimations != null)
            {
				currentCharacterAnimation = presetAnimations;
                GenerateAnimationsUI();
            }
        }
        else
        {
            ActivateCharacter();
        }

        presetOptions.CheckButtons();
    }

    //-----------------------------------------------------------------
    private void ActivateCharacter()
    {
        if (characterAnimation.gameObject.activeInHierarchy.Equals(false))
        {
            characterAnimation.gameObject.SetActive(true);
            currentCharacterAnimation = characterAnimation;
            GenerateAnimationsUI();

            if (presets != null)
                presets.DeactivatePreset();

            var characterRotate = GetComponent<CharacterRotate>();

            if (characterRotate != null)
                characterRotate.objectToRotate = characterAnimation.transform;

            if (presetOptions != null)
                presetOptions.assetList.value = 0;
        }
    }
}